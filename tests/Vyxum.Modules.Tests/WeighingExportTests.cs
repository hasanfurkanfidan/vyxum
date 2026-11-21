using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Shouldly;
using Vyxum.Modules.Weighing;
using Vyxum.Platform;
using Xunit;

namespace Vyxum.Modules.Tests;

public class WeighingExportTests
{
    private const string Scale = "scale-1";

    private static readonly Guid Pistachio = Guid.Parse("0e7d3f7a-5d0e-4a39-9f0e-000000000417");
    private static readonly Guid Cashew = Guid.Parse("0e7d3f7a-5d0e-4a39-9f0e-000000000203");

    private sealed record Sheet(Dictionary<string, string> Cells, Dictionary<string, uint?> Styles, List<string> Formats);

    private static Vyxum.Modules.Weighing.ProductSaved Product(Guid id, string code, string name, decimal price) =>
        new(id, code, name, price, null, 30, "28", true);

    private static async Task<long> WeighAsync(WeighingTestApp app, decimal value, string unit, Guid? product, string? lot)
    {
        app.Scale.Set(Scale, value, unit);
        var response = await app.Client.PostAsJsonAsync(
            "/api/weighing/records",
            new { requestId = Guid.NewGuid(), deviceId = Scale, productId = product, lot },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        app.Clock.Advance(TimeSpan.FromHours(1));
        return (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetInt64();
    }

    private static async Task<(HttpResponseMessage Response, byte[] Bytes)> ExportAsync(WeighingTestApp app, string query = "", string? language = null, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/weighing/records/export" + query);
        if (language is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", language);
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        var response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);
        return (response, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Opens the workbook and returns the cells by reference (text resolved), with the style's number format code.</summary>
    private static Sheet Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheetPart = workbookPart.WorksheetParts.Single();
        var stylesheet = workbookPart.WorkbookStylesPart!.Stylesheet!;
        var custom = stylesheet.NumberingFormats!.Elements<NumberingFormat>().ToDictionary(f => f.NumberFormatId!.Value, f => f.FormatCode!.Value!);
        var formats = stylesheet.CellFormats!.Elements<CellFormat>().ToList();

        var cells = new Dictionary<string, string>();
        var styles = new Dictionary<string, uint?>();
        var codes = new List<string>();
        foreach (var cell in sheetPart.Worksheet!.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>())
        {
            var reference = cell.CellReference!.Value!;
            if (cell.DataType?.Value == CellValues.InlineString)
            {
                cells[reference] = "s:" + cell.InlineString!.Text!.Text;
            }
            else
            {
                cells[reference] = "n:" + cell.CellValue!.Text;
            }

            styles[reference] = cell.StyleIndex?.Value;
            var id = cell.StyleIndex is { } index ? formats[(int)index.Value].NumberFormatId?.Value : null;
            codes.Add(reference + "=" + (id is { } f && custom.TryGetValue(f, out var code) ? code : "General"));
        }

        return new Sheet(cells, styles, codes);
    }

    private static string FormatOf(Sheet sheet, string reference) =>
        sheet.Formats.Single(f => f.StartsWith(reference + "=", StringComparison.Ordinal))[(reference.Length + 1)..];

    private static decimal Number(Sheet sheet, string reference)
    {
        sheet.Cells[reference].ShouldStartWith("n:");
        return decimal.Parse(sheet.Cells[reference][2..], CultureInfo.InvariantCulture);
    }

    private static async Task<(long A, long B, long C, long D)> SeedAsync(WeighingTestApp app)
    {
        await app.DeliverAsync(Product(Pistachio, "0417", "Pistachio", 780m));
        await app.DeliverAsync(Product(Cashew, "0203", "Cashew", 640m));
        var a = await WeighAsync(app, 0.512m, "kg", Cashew, "L-2");      // 327.68
        var b = await WeighAsync(app, 0.498m, "kg", Cashew, "L-2");      // 318.72, cancelled
        var c = await WeighAsync(app, 1.2475m, "kg", Pistachio, "L-3");  // 973.05
        var d = await WeighAsync(app, 0.25m, "kg", null, null);          // no amount
        (await app.Client.PostAsJsonAsync($"/api/weighing/records/{b}/cancel", new { reason = "Wrong" }, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        return (a, b, c, d);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Export_is_a_valid_xlsx_with_numeric_cells_and_totals_without_cancelled(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        var start = app.Clock.GetUtcNow().UtcDateTime;
        var (a, b, c, d) = await SeedAsync(app);

        var (response, bytes) = await ExportAsync(app);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar!.ShouldMatch(@"^weighing-records-\d{8}\.xlsx$");

        var sheet = Read(bytes);
        sheet.Cells["A1"].ShouldBe("s:Date and time");
        sheet.Cells["N1"].ShouldBe("s:Reason for cancellation");
        sheet.Cells.ContainsKey("O1").ShouldBeFalse(); // no users capability, no operator column

        // Oldest first: a, b (cancelled), c, d on rows 2 to 5.
        sheet.Cells["B2"].ShouldBe("s:" + a.ToString("D6", CultureInfo.InvariantCulture));
        sheet.Cells["B3"].ShouldBe("s:" + b.ToString("D6", CultureInfo.InvariantCulture));
        sheet.Cells["B5"].ShouldBe("s:" + d.ToString("D6", CultureInfo.InvariantCulture));
        sheet.Cells["C2"].ShouldBe("s:0203");
        sheet.Cells["D4"].ShouldBe("s:Pistachio");
        sheet.Cells["E4"].ShouldBe("s:L-3");
        sheet.Cells.ContainsKey("C5").ShouldBeFalse();

        // Time: a real date, UTC converted to the server's local time zone.
        var expected = TimeZoneInfo.ConvertTimeFromUtc(start, TimeZoneInfo.Local);
        DateTime.FromOADate((double)Number(sheet, "A2")).ShouldBe(expected, TimeSpan.FromSeconds(1));
        FormatOf(sheet, "A2").ShouldContain("yyyy");

        Number(sheet, "H2").ShouldBe(0.512m);
        FormatOf(sheet, "H2").ShouldBe("0.000");
        Number(sheet, "H4").ShouldBe(1.2475m);
        FormatOf(sheet, "H4").ShouldBe("0.0000");
        Number(sheet, "F4").ShouldBe(1.2475m);
        Number(sheet, "G4").ShouldBe(0m);
        Number(sheet, "J4").ShouldBe(780m);
        FormatOf(sheet, "J4").ShouldBe("0.00");
        Number(sheet, "K4").ShouldBe(973.05m);
        FormatOf(sheet, "K4").ShouldBe("0.00");
        sheet.Cells["I4"].ShouldBe("s:kg");
        sheet.Cells["L4"].ShouldBe("s:TRY");
        sheet.Cells["M2"].ShouldBe("s:saved");
        sheet.Cells["M3"].ShouldBe("s:cancelled");
        sheet.Cells["N3"].ShouldBe("s:Wrong");

        // Totals: label, valid count (3), net per unit and amount; the cancelled record is not summed.
        sheet.Cells["A6"].ShouldBe("s:Total");
        Number(sheet, "B6").ShouldBe(3m);
        Number(sheet, "H6").ShouldBe(2.0095m);
        sheet.Cells["I6"].ShouldBe("s:kg");
        Number(sheet, "K6").ShouldBe(1300.73m);
        sheet.Cells["A7"].ShouldBe("s:cancelled records are not included in totals");
        sheet.Cells.Keys.Any(k => k.EndsWith('8')).ShouldBeFalse();
        _ = c;
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Export_headers_follow_the_request_language(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        await SeedAsync(app);

        var (_, byHeader) = await ExportAsync(app, language: "tr-TR,tr;q=0.9");
        var tr = Read(byHeader);
        tr.Cells["A1"].ShouldBe("s:Tarih ve saat");
        tr.Cells["B1"].ShouldBe("s:Kayıt no");
        tr.Cells["I1"].ShouldBe("s:Birim");
        tr.Cells["M1"].ShouldBe("s:Durum");
        tr.Cells["M3"].ShouldBe("s:iptal");
        tr.Cells["A6"].ShouldBe("s:Toplam");
        tr.Cells["A7"].ShouldBe("s:iptaller toplama girmez");

        var (_, byCookie) = await ExportAsync(app, language: "en", cookie: ".AspNetCore.Culture=c%3Dtr%7Cuic%3Dtr");
        Read(byCookie).Cells["A1"].ShouldBe("s:Tarih ve saat");

        var (_, english) = await ExportAsync(app, language: "en-US");
        Read(english).Cells["A1"].ShouldBe("s:Date and time");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Export_uses_the_list_filters(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        var (a, b, _, _) = await SeedAsync(app);

        var (_, cashew) = await ExportAsync(app, $"?productId={Cashew}");
        var sheet = Read(cashew);
        sheet.Cells["B2"].ShouldBe("s:" + a.ToString("D6", CultureInfo.InvariantCulture));
        sheet.Cells["B3"].ShouldBe("s:" + b.ToString("D6", CultureInfo.InvariantCulture));
        sheet.Cells["A4"].ShouldBe("s:Total");
        Number(sheet, "B4").ShouldBe(1m);
        Number(sheet, "K4").ShouldBe(327.68m);

        var (_, valid) = await ExportAsync(app, "?includeCancelled=false");
        var validSheet = Read(valid);
        validSheet.Cells.Where(c => c.Key.StartsWith('M') && c.Key != "M1").Select(c => c.Value).ShouldBe(["s:saved", "s:saved", "s:saved"]);
        validSheet.Cells["A5"].ShouldBe("s:Total");
        validSheet.Cells.Values.ShouldNotContain("s:cancelled records are not included in totals");

        var (_, lot) = await ExportAsync(app, "?lot=L-3");
        Read(lot).Cells["A3"].ShouldBe("s:Total");

        var from = Uri.EscapeDataString(app.Clock.GetUtcNow().UtcDateTime.AddHours(1).ToString("O"));
        var (_, range) = await ExportAsync(app, $"?from={from}");
        Read(range).Cells["A2"].ShouldBe("s:Total");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Empty_export_has_header_and_totals(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);

        var (response, bytes) = await ExportAsync(app);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sheet = Read(bytes);
        sheet.Cells["A1"].ShouldBe("s:Date and time");
        sheet.Cells["A2"].ShouldBe("s:Total");
        Number(sheet, "B2").ShouldBe(0m);
        Number(sheet, "H2").ShouldBe(0m);
        sheet.Cells.ContainsKey("K2").ShouldBeFalse();
        sheet.Cells.Keys.Any(k => k.EndsWith('3')).ShouldBeFalse();
    }
}
