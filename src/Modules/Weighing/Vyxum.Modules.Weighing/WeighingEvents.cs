using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Weighing;

/// <summary>Published when a weighing record is saved.</summary>
/// <param name="RecordId">Record id.</param>
/// <param name="Number">Record number as shown, e.g. "000142".</param>
/// <param name="RequestId">The client's request id that created the record.</param>
/// <param name="CreatedAt">UTC time the record was saved.</param>
/// <param name="DeviceId">The scale the weight was read from.</param>
/// <param name="ProductId">Product, if any.</param>
/// <param name="ProductCode">Product PLU code at the time of weighing.</param>
/// <param name="ProductName">Product name at the time of weighing.</param>
/// <param name="Lot">Lot, if any.</param>
/// <param name="Gross">Gross weight in <paramref name="Unit"/>.</param>
/// <param name="Tare">Tare in <paramref name="Unit"/>.</param>
/// <param name="Net">Net weight in <paramref name="Unit"/>.</param>
/// <param name="Unit">Weight unit as reported by the scale (normalized when known, e.g. "kg"); null when it sent none.</param>
/// <param name="NetMilligrams">Net weight in milligrams; null when the unit is unknown.</param>
/// <param name="UnitPrice">Price per kg at the time of weighing.</param>
/// <param name="Amount">Net (kg) times unit price, rounded; null without a price.</param>
/// <param name="Currency">Currency of <paramref name="Amount"/>.</param>
/// <param name="Operator">Operator; null until a users module exists.</param>
[IntegrationEvent("weighing.recorded", 1)]
public sealed record WeighingRecorded(
    long RecordId,
    string Number,
    Guid RequestId,
    DateTime CreatedAt,
    string DeviceId,
    Guid? ProductId,
    string? ProductCode,
    string? ProductName,
    string? Lot,
    decimal Gross,
    decimal Tare,
    decimal Net,
    string? Unit,
    long? NetMilligrams,
    decimal? UnitPrice,
    decimal? Amount,
    string Currency,
    string? Operator) : IIntegrationEvent;

/// <summary>Published when a weighing record is cancelled.</summary>
/// <param name="RecordId">Record id.</param>
/// <param name="Number">Record number as shown, e.g. "000142".</param>
/// <param name="RequestId">The client's request id that created the record.</param>
/// <param name="CancelledAt">UTC time of the cancel.</param>
/// <param name="Reason">Reason, if given.</param>
[IntegrationEvent("weighing.cancelled", 1)]
public sealed record WeighingCancelled(
    long RecordId,
    string Number,
    Guid RequestId,
    DateTime CancelledAt,
    string? Reason) : IIntegrationEvent;
