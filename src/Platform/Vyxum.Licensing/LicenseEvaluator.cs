namespace Vyxum.Licensing;

/// <summary>The state of a license on a given day.</summary>
public enum LicenseStatus
{
    /// <summary>The day is before <see cref="LicensePayload.ValidFrom"/>.</summary>
    NotYetValid,

    /// <summary>The day is between <see cref="LicensePayload.ValidFrom"/> and <see cref="LicensePayload.ValidUntil"/> (both inclusive), or the license is perpetual.</summary>
    Valid,

    /// <summary>The license has ended but the grace period still runs: modules work with a warning.</summary>
    Grace,

    /// <summary>The grace period is over: licensed modules are read-only.</summary>
    ReadOnly,
}

/// <summary>What a single module may do under the license.</summary>
public enum ModuleLicenseStatus
{
    /// <summary>The module is not in the license, or the license has not started yet.</summary>
    NotLicensed,

    /// <summary>The module is licensed and the license is valid.</summary>
    Licensed,

    /// <summary>The module is licensed and the license is in its grace period.</summary>
    Grace,

    /// <summary>The module is licensed but the grace period is over: reads only.</summary>
    ReadOnly,
}

/// <summary>The result of evaluating a license at a point in time.</summary>
public sealed class LicenseEvaluation
{
    internal LicenseEvaluation(VerifiedLicense license, LicenseStatus status, DateTimeOffset effectiveNow, DateOnly effectiveDate, DateOnly? graceEndsOn)
    {
        License = license;
        Status = status;
        EffectiveNow = effectiveNow;
        EffectiveDate = effectiveDate;
        GraceEndsOn = graceEndsOn;
        GraceDaysLeft = status == LicenseStatus.Grace ? graceEndsOn!.Value.DayNumber - effectiveDate.DayNumber + 1 : null;
    }

    /// <summary>The evaluated license.</summary>
    public VerifiedLicense License { get; }

    /// <summary>The license state on <see cref="EffectiveDate"/>.</summary>
    public LicenseStatus Status { get; }

    /// <summary>The time used for the evaluation: the later of the clock and the previously seen time.</summary>
    public DateTimeOffset EffectiveNow { get; }

    /// <summary>The local calendar date of <see cref="EffectiveNow"/> that the license dates are compared with.</summary>
    public DateOnly EffectiveDate { get; }

    /// <summary>The value to persist as the latest seen time. Always equal to <see cref="EffectiveNow"/>.</summary>
    public DateTimeOffset LastSeen => EffectiveNow;

    /// <summary>The last day of the grace period (inclusive); <see langword="null"/> for a perpetual license.</summary>
    public DateOnly? GraceEndsOn { get; }

    /// <summary>
    /// During <see cref="LicenseStatus.Grace"/>: the grace days left, counting today. The first grace day gives
    /// <see cref="LicensePayload.GraceDays"/>, the last grace day gives 1. <see langword="null"/> in other states.
    /// </summary>
    public int? GraceDaysLeft { get; }

    /// <summary>What module <paramref name="moduleId"/> (its manifest id, compared ordinally) may do.</summary>
    public ModuleLicenseStatus IsModuleLicensed(string moduleId)
    {
        ArgumentNullException.ThrowIfNull(moduleId);
        if (!License.Payload.Modules.Contains(moduleId, StringComparer.Ordinal))
            return ModuleLicenseStatus.NotLicensed;
        return Status switch
        {
            LicenseStatus.Valid => ModuleLicenseStatus.Licensed,
            LicenseStatus.Grace => ModuleLicenseStatus.Grace,
            LicenseStatus.ReadOnly => ModuleLicenseStatus.ReadOnly,
            _ => ModuleLicenseStatus.NotLicensed,
        };
    }
}

/// <summary>
/// Decides the state of a verified license from the clock.
/// </summary>
/// <remarks>
/// <para>
/// Clock rule: the evaluation uses <c>effectiveNow = max(now, lastSeen)</c>, where <c>lastSeen</c> is the latest time
/// observed before (persisted by the caller from <see cref="LicenseEvaluation.LastSeen"/>). Turning the clock back
/// therefore never extends a license.
/// </para>
/// <para>
/// Date rule: license dates are calendar dates compared with the local date of <c>effectiveNow</c> in
/// <see cref="TimeProvider.LocalTimeZone"/> (the time zone of the computer running the product), so a license
/// changes state at local midnight. With <c>d</c> that local date:
/// <c>d &lt; validFrom</c> → <see cref="LicenseStatus.NotYetValid"/>;
/// <c>validUntil</c> empty or <c>d ≤ validUntil</c> → <see cref="LicenseStatus.Valid"/> (validUntil is inclusive:
/// the license works through that whole day);
/// <c>d ≤ validUntil + graceDays</c> → <see cref="LicenseStatus.Grace"/> (grace is <c>graceDays</c> full days after
/// validUntil; with 0 there is no grace);
/// otherwise <see cref="LicenseStatus.ReadOnly"/>.
/// </para>
/// </remarks>
public sealed class LicenseEvaluator(TimeProvider timeProvider)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>Evaluates <paramref name="license"/> now, given the latest previously seen time.</summary>
    public LicenseEvaluation Evaluate(VerifiedLicense license, DateTimeOffset? lastSeen)
    {
        ArgumentNullException.ThrowIfNull(license);
        var now = _timeProvider.GetUtcNow();
        var effectiveNow = lastSeen is { } seen && seen > now ? seen : now;
        var localNow = TimeZoneInfo.ConvertTime(effectiveNow, _timeProvider.LocalTimeZone);
        var date = DateOnly.FromDateTime(localNow.DateTime);

        var payload = license.Payload;
        DateOnly? graceEndsOn = null;
        LicenseStatus status;
        if (date < payload.ValidFrom)
        {
            status = LicenseStatus.NotYetValid;
        }
        else if (payload.ValidUntil is not { } until)
        {
            status = LicenseStatus.Valid;
        }
        else
        {
            // DayNumber arithmetic avoids overflowing DateOnly near its maximum.
            var daysAfterEnd = date.DayNumber - until.DayNumber;
            graceEndsOn = until.DayNumber + payload.GraceDays <= DateOnly.MaxValue.DayNumber
                ? until.AddDays(payload.GraceDays)
                : DateOnly.MaxValue;
            status = daysAfterEnd <= 0 ? LicenseStatus.Valid
                : daysAfterEnd <= payload.GraceDays ? LicenseStatus.Grace
                : LicenseStatus.ReadOnly;
        }

        return new LicenseEvaluation(license, status, effectiveNow, date, graceEndsOn);
    }
}
