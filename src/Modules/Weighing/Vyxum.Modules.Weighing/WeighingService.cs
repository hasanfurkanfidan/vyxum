using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Weighing;

/// <summary>Creates and cancels weighing records. Every change publishes its event in the same transaction.</summary>
/// <remarks>
/// Tare (decision V-29): the tare is kept in software. An explicit tare from the request (the screen's
/// "Take tare" captures the gross) wins; otherwise the product's package tare (kg) converted to the scale's
/// unit; otherwise 0. When the scale itself reports a tare or says the weight is net, the reading is
/// already net: its own tare is stored and no software tare is subtracted, so the tare is never taken twice.
/// </remarks>
public sealed class WeighingService(
    WeighingDbContext db,
    ILiveWeightSource weights,
    IOptions<WeighingOptions> options,
    TimeProvider time,
    ICurrentActor actor,
    IAuthorization authorization)
{
    private readonly WeighingOptions _options = options.Value;

    /// <summary>Reads the scale's latest weight, applies the rules and saves a record, or returns the record of an earlier request with the same id.</summary>
    public async Task<WeighingResult> CreateAsync(CreateRecordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.IsAllowed(WeighingPermissions.Record))
        {
            return WeighingResult.Forbidden();
        }

        var errors = new Dictionary<string, string[]>();
        var input = Validate(request, errors);
        if (input is null)
        {
            return WeighingResult.Invalid(errors);
        }

        // A repeated request (e.g. after a lost response) returns what was saved the first time.
        if (await FindByRequestIdAsync(input.RequestId, cancellationToken) is { } existing)
        {
            return WeighingResult.Ok(WeighingMapper.ToDto(existing), created: false);
        }

        var outcome = await BuildAsync(input, cancellationToken);
        if (outcome.Rejection is not null)
        {
            return WeighingResult.Rejected(outcome.Rejection);
        }

        var record = outcome.Record!;
        try
        {
            // The id comes from the identity column and the event carries it, so the record is inserted
            // first and the event added afterwards, both in one transaction.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.Records.Add(record);
            await db.SaveChangesAsync(cancellationToken);
            db.Publish(WeighingMapper.ToRecordedEvent(record, request.PrintLabel));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race against the same request arriving twice: the unique request id rejected this one.
            db.ChangeTracker.Clear();
            if (await FindByRequestIdAsync(input.RequestId, cancellationToken) is { } winner)
            {
                return WeighingResult.Ok(WeighingMapper.ToDto(winner), created: false);
            }

            throw;
        }

        return WeighingResult.Ok(WeighingMapper.ToDto(record), created: true);
    }

    /// <summary>Cancels a saved record.</summary>
    public async Task<WeighingResult> CancelAsync(long id, CancelRecordRequest? request, CancellationToken cancellationToken = default)
    {
        var record = await db.Records.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (record is null)
        {
            return WeighingResult.Missing();
        }

        if (!MayCancel(record))
        {
            return WeighingResult.Forbidden();
        }

        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
        if (reason is null && _options.RequireCancelReason)
        {
            return WeighingResult.Invalid(new() { ["reason"] = [WeighingErrors.CancelReasonRequired] });
        }

        if (reason?.Length > WeighingLimits.CancelReasonMaxLength)
        {
            return WeighingResult.Invalid(new() { ["reason"] = [WeighingErrors.CancelReasonTooLong] });
        }

        if (record.Status == WeighingStatus.Cancelled)
        {
            return WeighingResult.Conflict(WeighingErrors.AlreadyCancelled);
        }

        record.Status = WeighingStatus.Cancelled;
        record.CancelledAtUtc = time.GetUtcNow().UtcDateTime;
        record.CancelReason = reason;
        db.Publish(new WeighingCancelled(record.Id, WeighingMapper.Number(record.Id), record.RequestId, record.CancelledAtUtc.Value, reason));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Status is a concurrency token: another request cancelled the record first.
            db.ChangeTracker.Clear();
            return WeighingResult.Conflict(WeighingErrors.AlreadyCancelled);
        }

        return WeighingResult.Ok(WeighingMapper.ToDto(record), created: false);
    }

    /// <summary>Anyone with <c>weighing.cancel</c>; the operator who saved it within the window with <c>weighing.cancel.own</c>.</summary>
    private bool MayCancel(WeighingRecord record)
    {
        if (authorization.IsAllowed(WeighingPermissions.Cancel))
        {
            return true;
        }

        return authorization.IsAllowed(WeighingPermissions.CancelOwn)
               && actor.Person is { } person
               && record.OperatorId is not null
               && string.Equals(record.OperatorId, person.Id, StringComparison.Ordinal)
               && time.GetUtcNow().UtcDateTime - record.CreatedAtUtc <= _options.CancelOwnWindow;
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    private Task<WeighingRecord?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken) =>
        db.Records.AsNoTracking().SingleOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);

    private async Task<(WeighingRecord? Record, string? Rejection)> BuildAsync(Input input, CancellationToken cancellationToken)
    {
        // The reading: connected device, a value, in range, stable when required.
        var device = weights.GetDevice(input.DeviceId);
        if (device is null || device.State == DeviceConnectionState.Disconnected)
        {
            return (null, WeighingErrors.DeviceNotConnected);
        }

        var reading = weights.GetLatest(input.DeviceId);
        if (reading is null || IsTooOld(reading))
        {
            return (null, WeighingErrors.NoReading);
        }

        if (device.State != DeviceConnectionState.Connected)
        {
            return (null, WeighingErrors.DeviceNotConnected);
        }

        if (input.Observed is { } observed && !StillShows(reading, observed))
        {
            return (null, WeighingErrors.ReadingChanged);
        }

        switch (reading.State)
        {
            case LiveWeightState.Overload:
                return (null, WeighingErrors.Overload);
            case LiveWeightState.Underload:
                return (null, WeighingErrors.Underload);
            case LiveWeightState.Motion when _options.RequireStable:
                return (null, WeighingErrors.NotStable);
        }

        if (reading.Value is not { } value)
        {
            return (null, WeighingErrors.NoReading);
        }

        // The product, from the local copy (empty when no products module is loaded).
        WeighingProduct? product = null;
        if (input.ProductId is { } productId)
        {
            product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.ProductId == productId, cancellationToken);
            if (product is null)
            {
                return (null, WeighingErrors.UnknownProduct);
            }

            if (!product.IsActive)
            {
                return (null, WeighingErrors.InactiveProduct);
            }
        }

        var unit = WeightUnits.Normalize(reading.Unit);
        var unitKnown = WeightUnits.IsKnown(unit);
        var reported = RoundWeight(value);

        // Tare and net. See the class remarks for the precedence.
        decimal gross, tare, net;
        int tareScale;
        var deviceTare = reading.Tare is { } t ? RoundWeight(t) : 0m;
        if (reading.IsNet == true || deviceTare != 0m)
        {
            tare = deviceTare;
            tareScale = reading.Tare is null ? reported.Scale : tare.Scale;
            if (reading.IsNet == false)
            {
                gross = reported;
                net = gross - tare;
            }
            else
            {
                net = reported;
                gross = net + tare;
            }
        }
        else
        {
            gross = reported;
            if (input.Tare is { } explicitTare)
            {
                tare = explicitTare;
                tareScale = tare.Scale;
            }
            else if (product?.PackageTare is { } packageTare && packageTare != 0m)
            {
                if (!unitKnown)
                {
                    return (null, WeighingErrors.UnknownUnit);
                }

                tare = WeightUnits.TrimScale(decimal.Round(
                    WeightUnits.FromKilograms(packageTare, unit!), WeighingLimits.WeightMaxDecimals, MidpointRounding.AwayFromZero));
                tareScale = tare.Scale;
            }
            else
            {
                tare = 0m;
                tareScale = gross.Scale;
            }

            net = gross - tare;
        }

        var grossScale = gross.Scale;
        var netScale = Math.Max(grossScale, tareScale);

        // Rules on the net. The minimum is in kg, so a positive minimum needs a known unit.
        if (net <= 0m)
        {
            return (null, WeighingErrors.NetTooSmall);
        }

        if (_options.MinimumNet > 0m)
        {
            if (!unitKnown)
            {
                return (null, WeighingErrors.UnknownUnit);
            }

            if (WeightUnits.ToKilograms(net, unit!) < _options.MinimumNet)
            {
                return (null, WeighingErrors.NetTooSmall);
            }
        }

        // Amount = net in kg x price per kg, rounded as configured.
        decimal? amount = null;
        long? amountMinor = null;
        var unitPrice = product?.UnitPrice;
        if (unitPrice is { } price)
        {
            if (!unitKnown)
            {
                return (null, WeighingErrors.UnknownUnit);
            }

            var rounded = decimal.Round(WeightUnits.ToKilograms(net, unit!) * price, _options.AmountDecimals, _options.AmountRounding);
            amount = rounded;
            amountMinor = (long)(rounded * WeightUnits.Pow10(_options.AmountDecimals));
        }

        var record = new WeighingRecord
        {
            RequestId = input.RequestId,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
            DeviceId = input.DeviceId,
            ProductId = product?.ProductId,
            ProductCode = product?.Code,
            ProductName = product?.Name,
            Lot = input.Lot,
            Gross = gross,
            GrossScale = grossScale,
            Tare = tare,
            TareScale = tareScale,
            Net = net,
            NetUnits = (long)(net * WeightUnits.Pow10(netScale)),
            NetMilligrams = unitKnown ? (long)decimal.Round(net * WeightUnits.ToMilligrams(unit!), 0, MidpointRounding.AwayFromZero) : null,
            Unit = unit,
            UnitPrice = unitPrice,
            TargetWeight = product?.TargetWeight,
            LowerTolerance = product?.LowerTolerance,
            UpperTolerance = product?.UpperTolerance,
            Amount = amount,
            AmountMinor = amountMinor,
            AmountScale = _options.AmountDecimals,
            Currency = _options.Currency.Trim(),
            Status = WeighingStatus.Saved,
            Operator = Truncate(actor.Person?.DisplayName, WeighingLimits.OperatorMaxLength),
            OperatorId = actor.Person?.Id,
        };
        return (record, null);
    }

    /// <summary>
    /// True when the latest reading still has the value and unit the operator saw, and the operator's reading is recent
    /// enough (<see cref="WeighingOptions.MaxSaveDelay"/>).
    /// </summary>
    private bool StillShows(LiveWeight reading, Observed observed)
    {
        if (_options.MaxSaveDelay > TimeSpan.Zero && time.GetUtcNow() - observed.ReceivedAt > _options.MaxSaveDelay)
        {
            return false;
        }

        return reading.Value == observed.Value
            && string.Equals(WeightUnits.Normalize(reading.Unit), WeightUnits.Normalize(observed.Unit), StringComparison.Ordinal);
    }

    /// <summary>True when the reading is older than <see cref="WeighingOptions.MaxReadingAge"/> allows.</summary>
    private bool IsTooOld(LiveWeight reading) =>
        _options.MaxReadingAge > TimeSpan.Zero && time.GetUtcNow() - reading.ReceivedAt > _options.MaxReadingAge;

    private static decimal RoundWeight(decimal value) =>
        value.Scale > WeighingLimits.WeightMaxDecimals
            ? decimal.Round(value, WeighingLimits.WeightMaxDecimals, MidpointRounding.AwayFromZero)
            : value;

    private static Input? Validate(CreateRecordRequest request, Dictionary<string, string[]> errors)
    {
        if (request.RequestId is not { } requestId || requestId == Guid.Empty)
        {
            errors["requestId"] = [WeighingErrors.RequestIdRequired];
        }

        var deviceId = request.DeviceId?.Trim() ?? string.Empty;
        if (deviceId.Length == 0)
        {
            errors["deviceId"] = [WeighingErrors.DeviceIdRequired];
        }
        else if (deviceId.Length > WeighingLimits.DeviceIdMaxLength)
        {
            errors["deviceId"] = [WeighingErrors.DeviceIdTooLong];
        }

        var lot = string.IsNullOrWhiteSpace(request.Lot) ? null : request.Lot.Trim();
        if (lot?.Length > WeighingLimits.LotMaxLength)
        {
            errors["lot"] = [WeighingErrors.LotTooLong];
        }

        var tare = ParseWeight(request.Tare, "tare", errors);
        var observed = ParseObserved(request.Reading, errors);

        return errors.Count > 0
            ? null
            : new Input(request.RequestId!.Value, deviceId, request.ProductId, lot, tare, observed);
    }

    private static decimal? ParseWeight(string? text, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Strict: optional sign and decimal point only; no thousands separators, exponents or culture formats.
        var trimmed = text.Trim();
        if (!decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value.Scale > WeighingLimits.WeightMaxDecimals
            || Math.Abs(value) >= 1_000_000_000_000m)
        {
            errors[field] = [WeighingErrors.InvalidNumber];
            return null;
        }

        if (value < 0 || trimmed.StartsWith('-'))
        {
            errors[field] = [WeighingErrors.NegativeNotAllowed];
            return null;
        }

        return value;
    }

    private static Observed? ParseObserved(ObservedReading? reading, Dictionary<string, string[]> errors)
    {
        if (reading is null)
        {
            return null;
        }

        // Same strict format as the tare, but a reading may be negative (below zero on the scale).
        decimal? value = null;
        if (!decimal.TryParse(reading.Value?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || Math.Abs(parsed) >= 1_000_000_000_000m)
        {
            errors["reading.value"] = [WeighingErrors.InvalidNumber];
        }
        else
        {
            value = parsed;
        }

        if (reading.ReceivedAt is not { } receivedAt)
        {
            errors["reading.receivedAt"] = [WeighingErrors.InvalidNumber];
            return null;
        }

        return value is { } v ? new Observed(v, reading.Unit, receivedAt) : null;
    }

    private sealed record Observed(decimal Value, string? Unit, DateTimeOffset ReceivedAt);

    private sealed record Input(Guid RequestId, string DeviceId, Guid? ProductId, string? Lot, decimal? Tare, Observed? Observed);
}

/// <summary>Maps records to DTOs and events.</summary>
internal static class WeighingMapper
{
    public static string Number(long id) => id.ToString("D6", CultureInfo.InvariantCulture);

    public static int NetScale(WeighingRecord r) => Math.Max(r.GrossScale, r.TareScale);

    public static string? FormatPrice(decimal? value) => value?.ToString("0.00######", CultureInfo.InvariantCulture);

    public static WeighingRecordDto ToDto(WeighingRecord r) => new(
        r.Id,
        Number(r.Id),
        r.CreatedAtUtc,
        r.DeviceId,
        r.ProductId,
        r.ProductCode,
        r.ProductName,
        r.Lot,
        WeightUnits.Format(r.Gross, r.GrossScale),
        WeightUnits.Format(r.Tare, r.TareScale),
        WeightUnits.Format(r.Net, NetScale(r)),
        r.Unit,
        FormatPrice(r.UnitPrice),
        r.Amount is { } amount ? WeightUnits.Format(amount, r.AmountScale) : null,
        r.Currency,
        r.Status.ToString(),
        r.CancelledAtUtc,
        r.CancelReason,
        r.Operator,
        FormatPrice(r.TargetWeight),
        FormatPrice(r.LowerTolerance),
        FormatPrice(r.UpperTolerance));

    public static WeighingRecorded ToRecordedEvent(WeighingRecord r, bool labelRequested = false) => new(
        r.Id,
        Number(r.Id),
        r.RequestId,
        r.CreatedAtUtc,
        r.DeviceId,
        r.ProductId,
        r.ProductCode,
        r.ProductName,
        r.Lot,
        r.Gross,
        r.Tare,
        r.Net,
        r.Unit,
        r.NetMilligrams,
        r.UnitPrice,
        r.Amount,
        r.Currency,
        r.Operator,
        r.TargetWeight,
        r.LowerTolerance,
        r.UpperTolerance,
        labelRequested);
}
