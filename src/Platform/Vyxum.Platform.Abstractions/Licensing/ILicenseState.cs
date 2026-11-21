namespace Vyxum.Platform.Abstractions;

/// <summary>The kind of the installed license.</summary>
public enum LicenseKind
{
    /// <summary>A time-limited trial.</summary>
    Trial,

    /// <summary>A subscription with an end date.</summary>
    Subscription,

    /// <summary>A license without an end date.</summary>
    Perpetual,

    /// <summary>Not for resale (partners, demos).</summary>
    Nfr,
}

/// <summary>The overall state of the license of this installation.</summary>
public enum LicenseCondition
{
    /// <summary>No license file is installed (the free version).</summary>
    None,

    /// <summary>A license file is stored but it no longer verifies (for example a changed file). Treated as no license.</summary>
    Invalid,

    /// <summary>The license starts on a later day.</summary>
    NotYetValid,

    /// <summary>The license is valid.</summary>
    Valid,

    /// <summary>The license has ended and its grace period runs: licensed modules work with a warning.</summary>
    Grace,

    /// <summary>The grace period is over: licensed modules are read-only.</summary>
    ReadOnly,
}

/// <summary>What one module may do under the current license.</summary>
public enum ModuleLicenseState
{
    /// <summary>The module does not require a license (<see cref="ModuleManifest.RequiresLicense"/> is false).</summary>
    Free,

    /// <summary>The module is licensed and the license is valid.</summary>
    Licensed,

    /// <summary>The module is licensed and the license is in its grace period: it works, the user is warned.</summary>
    Grace,

    /// <summary>The license of the module has ended and the grace period is over: reads only, no new operations.</summary>
    ReadOnly,

    /// <summary>No valid license names the module (or the license has not started yet): the module does not work.</summary>
    Locked,
}

/// <summary>
/// A read-only view of the license of this installation, for modules. Every member reads the current state, which can
/// change between two calls (a license is uploaded or removed, a day passes); read what you need once per operation.
/// </summary>
/// <remarks>
/// The platform already enforces <see cref="ModuleManifest.RequiresLicense"/> on the module's endpoints and event
/// handlers. Paid modules use this view for details: the licensee to print, whether a device is a licensed station
/// (decision V-49), and <see cref="RawLicense"/> to verify the file again with their own embedded key.
/// </remarks>
public interface ILicenseState
{
    /// <summary>Whether a license file that verifies is installed (in any state, including read-only).</summary>
    bool HasLicense { get; }

    /// <summary>The overall state.</summary>
    LicenseCondition Status { get; }

    /// <summary>The licensee's name, or null without a license.</summary>
    string? LicenseeName { get; }

    /// <summary>The license kind, or null without a license.</summary>
    LicenseKind? Kind { get; }

    /// <summary>During <see cref="LicenseCondition.Grace"/>: the grace days left, counting today. Null in other states.</summary>
    int? GraceDaysLeft { get; }

    /// <summary>The number of licensed stations (devices); 0 without a license.</summary>
    int StationLimit { get; }

    /// <summary>The ids of the devices chosen as licensed stations; empty without a license.</summary>
    IReadOnlyList<string> LicensedStations { get; }

    /// <summary>The license file text exactly as stored, or null without a license.</summary>
    string? RawLicense { get; }

    /// <summary>
    /// What the module with manifest id <paramref name="moduleId"/> may do. A loaded module that does not require a
    /// license is <see cref="ModuleLicenseState.Free"/>; any other id is judged by the license alone.
    /// </summary>
    ModuleLicenseState GetModuleStatus(string moduleId);

    /// <summary>Whether device <paramref name="deviceId"/> is one of the licensed stations of a license that verifies.</summary>
    bool IsStationLicensed(string deviceId);
}
