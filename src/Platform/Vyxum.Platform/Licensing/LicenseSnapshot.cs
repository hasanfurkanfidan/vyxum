using Vyxum.Licensing;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>An immutable picture of the installed license at one moment, as computed by <see cref="LicenseManager"/>.</summary>
internal sealed class LicenseSnapshot
{
    private readonly HashSet<string> _stations;
    private readonly ModuleSet _modules;

    private LicenseSnapshot(
        ModuleSet modules,
        LicenseCondition status,
        string? errorCode,
        LicenseEvaluation? evaluation,
        bool clockBehind,
        bool clockAhead,
        IReadOnlyList<string> stations,
        DateOnly computedForDate,
        DateTimeOffset computedAt)
    {
        _modules = modules;
        Status = status;
        ErrorCode = errorCode;
        Evaluation = evaluation;
        ClockBehind = clockBehind;
        ClockAhead = clockAhead;
        Stations = stations;
        _stations = new HashSet<string>(stations, StringComparer.Ordinal);
        ComputedForDate = computedForDate;
        ComputedAt = computedAt;
    }

    /// <summary>The overall state.</summary>
    public LicenseCondition Status { get; }

    /// <summary>Whether a license file is stored (valid or not).</summary>
    public bool HasStoredFile => Status != LicenseCondition.None;

    /// <summary>The error code of a stored file that does not verify (for example <c>invalid_signature</c>).</summary>
    public string? ErrorCode { get; }

    /// <summary>The evaluation of a stored file that verifies; null otherwise.</summary>
    public LicenseEvaluation? Evaluation { get; }

    /// <summary>The verified license; null without a valid file.</summary>
    public VerifiedLicense? License => Evaluation?.License;

    /// <summary>True when the clock is more than a day behind the latest time seen for this license.</summary>
    public bool ClockBehind { get; }

    /// <summary>
    /// True while the clock is more than <see cref="LicenseManager.ForwardJumpTolerance"/> ahead of the latest time seen
    /// for this license and the jump is not yet confirmed (see <see cref="LicenseManager"/>, clock rule).
    /// </summary>
    public bool ClockAhead { get; }

    /// <summary>Licensed station (device) ids: stored choice, limited to existing devices and the station limit.</summary>
    public IReadOnlyList<string> Stations { get; }

    /// <summary>The local date (of the platform clock) this snapshot was computed for.</summary>
    public DateOnly ComputedForDate { get; }

    /// <summary>When this snapshot was computed (platform clock).</summary>
    public DateTimeOffset ComputedAt { get; }

    /// <summary>The station limit; 0 without a valid license.</summary>
    public int StationLimit => License?.Payload.Stations ?? 0;

    /// <summary>What module <paramref name="moduleId"/> may do; see <see cref="ILicenseState.GetModuleStatus"/>.</summary>
    public ModuleLicenseState GetModuleStatus(string moduleId)
    {
        ArgumentNullException.ThrowIfNull(moduleId);
        if (_modules.IsFree(moduleId))
        {
            return ModuleLicenseState.Free;
        }

        return Evaluation?.IsModuleLicensed(moduleId) switch
        {
            ModuleLicenseStatus.Licensed => ModuleLicenseState.Licensed,
            ModuleLicenseStatus.Grace => ModuleLicenseState.Grace,
            ModuleLicenseStatus.ReadOnly => ModuleLicenseState.ReadOnly,
            _ => ModuleLicenseState.Locked,
        };
    }

    /// <summary>
    /// Whether <paramref name="deviceId"/> is a licensed station of a license that is in force: valid or in its grace
    /// period. Not yet valid and read-only licenses license no station.
    /// </summary>
    public bool IsStationLicensed(string deviceId) =>
        Status is LicenseCondition.Valid or LicenseCondition.Grace && _stations.Contains(deviceId);

    /// <summary>A snapshot without a stored file.</summary>
    public static LicenseSnapshot None(ModuleSet modules, DateOnly date, DateTimeOffset at) =>
        new(modules, LicenseCondition.None, null, null, false, false, [], date, at);

    /// <summary>A snapshot of a stored file that does not verify.</summary>
    public static LicenseSnapshot Invalid(ModuleSet modules, string errorCode, DateOnly date, DateTimeOffset at) =>
        new(modules, LicenseCondition.Invalid, errorCode, null, false, false, [], date, at);

    /// <summary>A snapshot of a verified, evaluated license.</summary>
    public static LicenseSnapshot Verified(
        ModuleSet modules,
        LicenseEvaluation evaluation,
        bool clockBehind,
        bool clockAhead,
        IReadOnlyList<string> stations,
        DateOnly date,
        DateTimeOffset at)
    {
        var status = evaluation.Status switch
        {
            LicenseStatus.NotYetValid => LicenseCondition.NotYetValid,
            LicenseStatus.Valid => LicenseCondition.Valid,
            LicenseStatus.Grace => LicenseCondition.Grace,
            LicenseStatus.ReadOnly => LicenseCondition.ReadOnly,
            _ => throw new InvalidOperationException($"Unknown license status {evaluation.Status}."),
        };
        return new LicenseSnapshot(modules, status, null, evaluation, clockBehind, clockAhead, stations, date, at);
    }

    /// <summary>A short text that changes whenever what the license allows changes; see <see cref="LicenseManager.Version"/>.</summary>
    public string Signature =>
        $"{Status}|{License?.Payload.LicenseId}|{string.Join(',', Stations)}";

    /// <summary>The loaded modules, as far as licensing cares.</summary>
    internal sealed class ModuleSet(ModuleCatalog catalog)
    {
        private readonly HashSet<string> _free = catalog.Modules
            .Where(m => !m.Manifest.RequiresLicense)
            .Select(m => m.Manifest.Id)
            .ToHashSet(StringComparer.Ordinal);

        /// <summary>True for a loaded module that does not require a license.</summary>
        public bool IsFree(string moduleId) => _free.Contains(moduleId);
    }
}
