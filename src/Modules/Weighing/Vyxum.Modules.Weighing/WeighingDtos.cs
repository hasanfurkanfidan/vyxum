namespace Vyxum.Modules.Weighing;

/// <summary>A weighing record as returned by the API. Weights and money are invariant-culture strings that keep their decimals.</summary>
/// <param name="Id">Record id.</param>
/// <param name="Number">Record number: the id zero-padded to 6 digits, e.g. "000142".</param>
/// <param name="CreatedAt">UTC time the record was saved.</param>
/// <param name="DeviceId">The scale.</param>
/// <param name="ProductId">Product id, if any.</param>
/// <param name="ProductCode">Product code at the time of weighing.</param>
/// <param name="ProductName">Product name at the time of weighing.</param>
/// <param name="Lot">Lot.</param>
/// <param name="Gross">Gross weight, e.g. "1.260".</param>
/// <param name="Tare">Tare, e.g. "0.012".</param>
/// <param name="Net">Net weight, e.g. "1.248".</param>
/// <param name="Unit">Weight unit, e.g. "kg"; null when the scale sent none.</param>
/// <param name="UnitPrice">Price per kg, e.g. "780.00".</param>
/// <param name="Amount">Amount, e.g. "973.44".</param>
/// <param name="Currency">Currency, e.g. "TRY".</param>
/// <param name="Status"><c>Saved</c> or <c>Cancelled</c>.</param>
/// <param name="CancelledAt">UTC time of the cancel.</param>
/// <param name="CancelReason">Cancel reason.</param>
/// <param name="Operator">Operator; null until a users module exists.</param>
/// <param name="TargetWeight">Target weight in kg on the product when the record was saved, e.g. "0.250"; null if none.</param>
/// <param name="LowerTolerance">Allowed amount below the target in kg when the record was saved.</param>
/// <param name="UpperTolerance">Allowed amount above the target in kg when the record was saved.</param>
public sealed record WeighingRecordDto(
    long Id,
    string Number,
    DateTime CreatedAt,
    string DeviceId,
    Guid? ProductId,
    string? ProductCode,
    string? ProductName,
    string? Lot,
    string Gross,
    string Tare,
    string Net,
    string? Unit,
    string? UnitPrice,
    string? Amount,
    string Currency,
    string Status,
    DateTime? CancelledAt,
    string? CancelReason,
    string? Operator,
    string? TargetWeight,
    string? LowerTolerance,
    string? UpperTolerance);

/// <summary>Body of <c>POST /api/weighing/records</c>.</summary>
/// <param name="RequestId">Client-generated id; repeating it returns the record saved the first time.</param>
/// <param name="DeviceId">The scale to read.</param>
/// <param name="ProductId">Product from the Weighing module's product list, optional.</param>
/// <param name="Lot">Lot, optional.</param>
/// <param name="Tare">Station tare in the scale's unit as an invariant decimal string; overrides the product's package tare.</param>
/// <param name="Reading">
/// The reading the operator saw when saving, optional. When sent, the record is saved only if the scale's latest
/// reading still has this value and unit and the request arrives within <see cref="WeighingOptions.MaxSaveDelay"/> of
/// it; otherwise it is rejected with <see cref="WeighingErrors.ReadingChanged"/>. A retried request then never records
/// the next load on the scale.
/// </param>
/// <param name="PrintLabel">
/// True for "Save and print label": the saved record's event asks a label module to print. Ignored for a repeated
/// request, so a retry never asks for a second label.
/// </param>
public sealed record CreateRecordRequest(
    Guid? RequestId, string? DeviceId, Guid? ProductId, string? Lot, string? Tare, ObservedReading? Reading = null, bool PrintLabel = false);

/// <summary>A reading as the weighing screen showed it, sent with <see cref="CreateRecordRequest"/>.</summary>
/// <param name="Value">The value as an invariant decimal string, as the device endpoints sent it.</param>
/// <param name="Unit">The unit, as the device endpoints sent it.</param>
/// <param name="ReceivedAt">When the web application received the reading (<c>receivedAt</c> of the live weight).</param>
public sealed record ObservedReading(string? Value, string? Unit, DateTimeOffset? ReceivedAt);

/// <summary>Body of <c>POST /api/weighing/records/{id}/cancel</c>.</summary>
/// <param name="Reason">Why the record is cancelled.</param>
public sealed record CancelRecordRequest(string? Reason);

/// <summary>Net total of one unit.</summary>
/// <param name="Unit">Unit; null for records without a unit.</param>
/// <param name="Value">Sum of the net weights, e.g. "6.248".</param>
public sealed record NetTotalDto(string? Unit, string Value);

/// <summary>Totals of the filtered records. Cancelled records are not included in the sums.</summary>
/// <param name="ValidCount">Saved records.</param>
/// <param name="CancelledCount">Cancelled records.</param>
/// <param name="Net">Net totals per unit, ordered by unit.</param>
/// <param name="Amount">Sum of the amounts; null when no record has an amount.</param>
public sealed record WeighingTotalsDto(int ValidCount, int CancelledCount, IReadOnlyList<NetTotalDto> Net, string? Amount);

/// <summary>A page of records with the totals of every filtered record.</summary>
/// <param name="Items">Records, newest first.</param>
/// <param name="Totals">Totals of all filtered records, not only this page.</param>
public sealed record WeighingRecordListDto(IReadOnlyList<WeighingRecordDto> Items, WeighingTotalsDto Totals);

/// <summary>A product from the Weighing module's copy of the catalog.</summary>
/// <param name="Id">Product id.</param>
/// <param name="Code">PLU code.</param>
/// <param name="Name">Name.</param>
/// <param name="UnitPrice">Price per kg, e.g. "780.00".</param>
/// <param name="PackageTare">Package tare in kg, e.g. "0.012".</param>
/// <param name="ShelfLifeDays">Shelf life in days.</param>
/// <param name="BarcodePrefix">Barcode prefix.</param>
/// <param name="TargetWeight">Target weight in kg, e.g. "0.250".</param>
/// <param name="LowerTolerance">Allowed amount below the target in kg.</param>
/// <param name="UpperTolerance">Allowed amount above the target in kg.</param>
public sealed record WeighingProductDto(
    Guid Id,
    string Code,
    string Name,
    string? UnitPrice,
    string? PackageTare,
    int? ShelfLifeDays,
    string? BarcodePrefix,
    string? TargetWeight,
    string? LowerTolerance,
    string? UpperTolerance);

/// <summary>Weighing settings for the screen.</summary>
/// <param name="RequireStable">Only a stable weight can be recorded.</param>
/// <param name="MinimumNet">Smallest net in kg, e.g. "0"; the net must always be greater than 0.</param>
/// <param name="RequireCancelReason">A cancel needs a reason.</param>
/// <param name="Currency">Currency code.</param>
/// <param name="AmountDecimals">Decimals of amounts.</param>
/// <param name="CanPrintLabel">A labels module is loaded (<c>labels.print</c>).</param>
/// <param name="ShowOperator">A users module is loaded (<c>identity.users</c>).</param>
/// <param name="ProductsAvailable">A product catalog is loaded (<c>products.catalog</c>).</param>
public sealed record WeighingSettingsDto(
    bool RequireStable,
    string MinimumNet,
    bool RequireCancelReason,
    string Currency,
    int AmountDecimals,
    bool CanPrintLabel,
    bool ShowOperator,
    bool ProductsAvailable);

/// <summary>Outcome of a weighing command.</summary>
public sealed class WeighingResult
{
    private WeighingResult()
    {
    }

    /// <summary>The record, when the command succeeded.</summary>
    public WeighingRecordDto? Record { get; private init; }

    /// <summary>True when a new record was created (201); false when an existing one is returned (200).</summary>
    public bool Created { get; private init; }

    /// <summary>True when the record id is unknown.</summary>
    public bool NotFound { get; private init; }

    /// <summary>Validation errors keyed by camelCase field name; values are resx keys.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; private init; }

    /// <summary>Resx key of the business rule that rejected the command.</summary>
    public string? RejectionCode { get; private init; }

    /// <summary>True when the rejection is a conflict with the record's state (409) rather than a rule (422).</summary>
    public bool IsConflict { get; private init; }

    /// <summary>True when the current person may not do this (403).</summary>
    public bool IsForbidden { get; private init; }

    internal static WeighingResult Forbidden() => new() { RejectionCode = WeighingErrors.NotAllowed, IsForbidden = true };

    internal static WeighingResult Ok(WeighingRecordDto record, bool created) => new() { Record = record, Created = created };

    internal static WeighingResult Missing() => new() { NotFound = true };

    internal static WeighingResult Invalid(Dictionary<string, string[]> errors) => new() { Errors = errors };

    internal static WeighingResult Rejected(string code) => new() { RejectionCode = code };

    internal static WeighingResult Conflict(string code) => new() { RejectionCode = code, IsConflict = true };
}

/// <summary>Resx keys of errors, with the module prefix, as returned in <c>{ code }</c> and validation problems.</summary>
public static class WeighingErrors
{
    /// <summary>The signed-in person lacks the permission.</summary>
    public const string NotAllowed = "weighing.NotAllowed";

    /// <summary>The scale has not sent a weight.</summary>
    public const string NoReading = "weighing.NoReading";

    /// <summary>The scale is unknown or not connected.</summary>
    public const string DeviceNotConnected = "weighing.DeviceNotConnected";

    /// <summary>The weight is in motion and a stable weight is required.</summary>
    public const string NotStable = "weighing.NotStable";

    /// <summary>The scale reports overload.</summary>
    public const string Overload = "weighing.Overload";

    /// <summary>The scale reports underload.</summary>
    public const string Underload = "weighing.Underload";

    /// <summary>The net is not greater than 0 or below the configured minimum.</summary>
    public const string NetTooSmall = "weighing.NetTooSmall";

    /// <summary>The product is not in the Weighing module's product list.</summary>
    public const string UnknownProduct = "weighing.UnknownProduct";

    /// <summary>The product is inactive.</summary>
    public const string InactiveProduct = "weighing.InactiveProduct";

    /// <summary>A conversion is needed (package tare, price, minimum net) but the scale's unit is unknown.</summary>
    public const string UnknownUnit = "weighing.UnknownUnit";

    /// <summary>
    /// The scale's weight is no longer the one the operator saw, or the save arrived too late to be sure it is.
    /// </summary>
    public const string ReadingChanged = "weighing.ReadingChanged";

    /// <summary>The record is already cancelled.</summary>
    public const string AlreadyCancelled = "weighing.AlreadyCancelled";

    /// <summary>A cancel reason is required.</summary>
    public const string CancelReasonRequired = "weighing.CancelReasonRequired";

    /// <summary>The cancel reason is too long.</summary>
    public const string CancelReasonTooLong = "weighing.CancelReasonTooLong";

    /// <summary>The request id is missing.</summary>
    public const string RequestIdRequired = "weighing.RequestIdRequired";

    /// <summary>The device id is missing.</summary>
    public const string DeviceIdRequired = "weighing.DeviceIdRequired";

    /// <summary>The device id is too long.</summary>
    public const string DeviceIdTooLong = "weighing.DeviceIdTooLong";

    /// <summary>A number could not be read.</summary>
    public const string InvalidNumber = "weighing.InvalidNumber";

    /// <summary>A number is negative.</summary>
    public const string NegativeNotAllowed = "weighing.NegativeNotAllowed";

    /// <summary>The export has more records than the maximum.</summary>
    public const string ExportTooLarge = "weighing.ExportTooLarge";

    /// <summary>The lot is too long.</summary>
    public const string LotTooLong = "weighing.LotTooLong";
}
