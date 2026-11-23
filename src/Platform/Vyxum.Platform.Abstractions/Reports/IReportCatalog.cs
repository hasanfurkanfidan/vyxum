namespace Vyxum.Platform.Abstractions;

/// <summary>The reports declared by the loaded modules (<see cref="ModuleManifest.Reports"/>).</summary>
/// <remarks>
/// The list holds every loaded module's reports, whatever the license says. A reader that shows them decides per
/// request which modules may run now (<see cref="ILicenseState.GetModuleStatus"/>), because the license can change while
/// the application runs.
/// </remarks>
public interface IReportCatalog
{
    /// <summary>Every declared report, ordered by module id and then in declaration order.</summary>
    IReadOnlyList<DeclaredReport> Reports { get; }
}

/// <summary>A report and the module that declares it.</summary>
/// <param name="ModuleId">Manifest id of the declaring module.</param>
/// <param name="RequiresLicense">Whether that module requires a license (<see cref="ModuleManifest.RequiresLicense"/>).</param>
/// <param name="DisplayNameKey">That module's <see cref="ModuleManifest.DisplayNameKey"/>, for grouping by source.</param>
/// <param name="Definition">The declaration.</param>
public sealed record DeclaredReport(string ModuleId, bool RequiresLicense, string? DisplayNameKey, ReportDefinition Definition);
