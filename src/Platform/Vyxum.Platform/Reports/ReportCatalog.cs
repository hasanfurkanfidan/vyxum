using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>The reports of the loaded modules, read from their manifests once at startup.</summary>
internal sealed class ReportCatalog(ModuleCatalog modules) : IReportCatalog
{
    public IReadOnlyList<DeclaredReport> Reports { get; } = modules.Modules
        .OrderBy(m => m.Manifest.Id, StringComparer.Ordinal)
        .SelectMany(m => m.Manifest.Reports.Select(r => new DeclaredReport(m.Manifest.Id, m.Manifest.RequiresLicense, m.Manifest.DisplayNameKey, r)))
        .ToList();
}
