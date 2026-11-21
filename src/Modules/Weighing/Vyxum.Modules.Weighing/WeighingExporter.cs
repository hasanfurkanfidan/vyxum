using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Vyxum.Modules.Weighing;

/// <summary>
/// Writes the filtered records to an .xlsx file. Records are streamed row by row (SAX style) into a temporary file,
/// so a large export does not build the sheet in memory. Weights and money are numeric cells with a number format
/// that shows the record's decimals. The time is stored as an Excel date-time converted from UTC to the server's local time zone.
/// </summary>
public sealed class WeighingExporter(WeighingQueries queries)
{
    /// <summary>Media type of the exported file.</summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const uint HeaderStyle = 1;
    private const uint DateTimeStyle = 2;
    private const int MaxDecimals = 8;
    private const uint NumberStyleBase = 3;
    private const uint BoldNumberStyleBase = NumberStyleBase + MaxDecimals + 1;
    private const uint BoldStyle = BoldNumberStyleBase + MaxDecimals + 1;

    /// <summary>Writes the export and returns it as a stream positioned at its start; disposing the stream deletes the temporary file.</summary>
    public async Task<Stream> ExportAsync(WeighingRecordFilter filter, CultureInfo culture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(culture);

        string Text(string key) => WeighingStrings.Get(key, culture) ?? key;

        var showOperator = queries.Settings().ShowOperator;
        var totals = await queries.TotalsAsync(filter, cancellationToken);

        var stream = new FileStream(
            Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, autoSave: true))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook(
                    new Sheets(new Sheet { Id = workbookPart.GetIdOfPart(AddSheetPart(workbookPart)), SheetId = 1, Name = SheetName(Text("RecordsBreadcrumb")) }));
                AddStyles(workbookPart);

                var sheetPart = workbookPart.WorksheetParts.Single();
                using var writer = OpenXmlWriter.Create(sheetPart);
                writer.WriteStartElement(new Worksheet());
                writer.WriteElement(new SheetViews(new SheetView(
                    new Pane { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen })
                { WorkbookViewId = 0U }));
                writer.WriteElement(ColumnWidths(showOperator));
                writer.WriteStartElement(new SheetData());

                var columns = 0;
                var rowNumber = 1U;

                // Header.
                var headers = new List<string>
                {
                    Text("DateTime"), Text("RecordNo"), Text("Plu"), Text("Product"), Text("Lot"),
                    Text("Gross"), Text("Tare"), Text("Net"), Text("Unit"), Text("UnitPrice"),
                    Text("Amount"), Text("Currency"), Text("Status"), Text("CancelReason"),
                    Text("TargetWeight"), Text("LowerTolerance"), Text("UpperTolerance"),
                };
                if (showOperator)
                {
                    headers.Add(Text("Operator"));
                }

                columns = headers.Count;
                WriteRow(writer, rowNumber++, headers.Select((h, i) => Cell.Text(i, h, HeaderStyle)));

                var savedText = Text("Saved");
                var cancelledText = Text("Cancelled");
                await foreach (var r in queries.StreamItems(filter).WithCancellation(cancellationToken))
                {
                    var netScale = WeighingMapper.NetScale(r);
                    var cells = new List<Cell>(columns)
                    {
                        Cell.Number(0, ToExcelDate(r.CreatedAtUtc), DateTimeStyle),
                        Cell.Text(1, WeighingMapper.Number(r.Id)),
                        Cell.Text(2, r.ProductCode),
                        Cell.Text(3, r.ProductName),
                        Cell.Text(4, r.Lot),
                        Cell.Decimal(5, r.Gross, r.GrossScale),
                        Cell.Decimal(6, r.Tare, r.TareScale),
                        Cell.Decimal(7, r.Net, netScale),
                        Cell.Text(8, r.Unit),
                        r.UnitPrice is { } price ? Cell.Decimal(9, price, ParseInvariant(WeighingMapper.FormatPrice(price)!).Decimals) : Cell.Empty(9),
                        r.Amount is { } amount ? Cell.Decimal(10, amount, r.AmountScale) : Cell.Empty(10),
                        Cell.Text(11, r.Currency),
                        Cell.Text(12, r.Status == WeighingStatus.Cancelled ? cancelledText : savedText),
                        Cell.Text(13, r.CancelReason),
                        TargetCell(14, r.TargetWeight),
                        TargetCell(15, r.LowerTolerance),
                        TargetCell(16, r.UpperTolerance),
                    };
                    if (showOperator)
                    {
                        cells.Add(Cell.Text(17, r.Operator));
                    }

                    WriteRow(writer, rowNumber++, cells);
                }

                // Totals: one row per unit; the first also carries the valid count and the amount.
                var totalLabel = Text("Total");
                var netTotals = totals.Net.Count > 0 ? totals.Net : [new NetTotalDto(null, "0")];
                var first = true;
                foreach (var net in netTotals)
                {
                    var cells = new List<Cell> { Cell.Text(0, first ? totalLabel : null, BoldStyle) };
                    if (first)
                    {
                        cells.Add(Cell.Number(1, totals.ValidCount.ToString(CultureInfo.InvariantCulture), BoldNumberStyleBase));
                    }

                    var (netValue, netDecimals) = ParseInvariant(net.Value);
                    cells.Add(Cell.Decimal(7, netValue, netDecimals, bold: true));
                    cells.Add(Cell.Text(8, net.Unit, BoldStyle));
                    if (first && totals.Amount is { } amountText)
                    {
                        var (amountValue, amountDecimals) = ParseInvariant(amountText);
                        cells.Add(Cell.Decimal(10, amountValue, amountDecimals, bold: true));
                    }

                    WriteRow(writer, rowNumber++, cells);
                    first = false;
                }

                if (filter.IncludeCancelled && totals.CancelledCount > 0)
                {
                    WriteRow(writer, rowNumber++, [Cell.Text(0, Text("CancelledNotInTotals"))]);
                }

                writer.WriteEndElement(); // SheetData
                writer.WriteEndElement(); // Worksheet
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static Cell TargetCell(int column, decimal? value) =>
        value is { } v ? Cell.Decimal(column, v, ParseInvariant(WeighingMapper.FormatPrice(v)!).Decimals) : Cell.Empty(column);

    private static WorksheetPart AddSheetPart(WorkbookPart workbookPart) => workbookPart.AddNewPart<WorksheetPart>();

    private static string SheetName(string name)
    {
        var cleaned = new string([.. name.Where(c => !"[]:*?/\\".Contains(c))]);
        return cleaned.Length == 0 ? "Records" : cleaned[..Math.Min(cleaned.Length, 31)];
    }

    /// <summary>Excel date-time (days since 1900) of a UTC time, shown in the server's local time zone.</summary>
    private static string ToExcelDate(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local)
            .ToOADate().ToString("R", CultureInfo.InvariantCulture);

    private static (decimal Value, int Decimals) ParseInvariant(string text)
    {
        var value = decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        return (value, dot < 0 ? 0 : text.Length - dot - 1);
    }

    private static Columns ColumnWidths(bool showOperator)
    {
        double[] widths = [20, 11, 10, 28, 14, 11, 11, 11, 8, 12, 13, 10, 14, 30, 14, 14, 14, 18];
        var count = showOperator ? widths.Length : widths.Length - 1;
        var columns = new Columns();
        for (var i = 0; i < count; i++)
        {
            columns.Append(new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = widths[i], CustomWidth = true });
        }

        return columns;
    }

    private static void WriteRow(OpenXmlWriter writer, uint rowNumber, IEnumerable<Cell> cells)
    {
        writer.WriteStartElement(new Row { RowIndex = rowNumber });
        foreach (var cell in cells)
        {
            cell.Write(writer, rowNumber);
        }

        writer.WriteEndElement();
    }

    private static void AddStyles(WorkbookPart workbookPart)
    {
        var numberFormats = new NumberingFormats();
        var formats = new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1U, ApplyFont = true },
            new CellFormat { NumberFormatId = 164U, ApplyNumberFormat = true });
        numberFormats.Append(new NumberingFormat { NumberFormatId = 164U, FormatCode = "yyyy\\-mm\\-dd\\ hh:mm:ss" });

        for (var bold = 0; bold < 2; bold++)
        {
            for (var decimals = 0; decimals <= MaxDecimals; decimals++)
            {
                var id = 165U + (uint)(bold * (MaxDecimals + 1) + decimals);
                numberFormats.Append(new NumberingFormat { NumberFormatId = id, FormatCode = decimals == 0 ? "0" : "0." + new string('0', decimals) });
                formats.Append(new CellFormat { NumberFormatId = id, FontId = (uint)bold, ApplyNumberFormat = true, ApplyFont = bold == 1 });
            }
        }

        formats.Append(new CellFormat { FontId = 1U, ApplyFont = true }); // BoldStyle
        numberFormats.Count = (uint)numberFormats.ChildElements.Count;
        formats.Count = (uint)formats.ChildElements.Count;

        workbookPart.AddNewPart<WorkbookStylesPart>().Stylesheet = new Stylesheet(
            numberFormats,
            new Fonts(new Font(), new Font(new Bold())) { Count = 2U },
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2U },
            new Borders(new Border()) { Count = 1U },
            new CellStyleFormats(new CellFormat()) { Count = 1U },
            formats);
    }

    /// <summary>A cell to write: text, a number, or empty.</summary>
    private sealed class Cell
    {
        private int _column;
        private string? _value;
        private bool _isText;
        private uint _style;

        public static Cell Text(int column, string? value, uint style = 0) =>
            new() { _column = column, _value = string.IsNullOrEmpty(value) ? null : value, _isText = true, _style = style };

        public static Cell Number(int column, string invariantValue, uint style) =>
            new() { _column = column, _value = invariantValue, _style = style };

        public static Cell Empty(int column) => new() { _column = column };

        public static Cell Decimal(int column, decimal value, int decimals, bool bold = false) =>
            Number(
                column,
                value.ToString(CultureInfo.InvariantCulture),
                (bold ? BoldNumberStyleBase : NumberStyleBase) + (uint)Math.Clamp(decimals, 0, MaxDecimals));

        public void Write(OpenXmlWriter writer, uint rowNumber)
        {
            var reference = $"{(char)('A' + _column)}{rowNumber}";
            if (_value is null)
            {
                // Empty cells are skipped; styled empty cells are not needed.
                return;
            }

            var cell = new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference = reference };
            if (_style != 0)
            {
                cell.StyleIndex = _style;
            }

            if (_isText)
            {
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(_value) { Space = SpaceProcessingModeValues.Preserve });
            }
            else
            {
                cell.CellValue = new CellValue(_value);
            }

            writer.WriteElement(cell);
        }
    }
}
