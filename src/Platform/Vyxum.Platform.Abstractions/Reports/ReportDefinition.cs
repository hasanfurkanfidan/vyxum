namespace Vyxum.Platform.Abstractions;

/// <summary>
/// A report a module offers, as a reports module lists it in its catalog (Reports design T1). The report itself stays
/// on its own module's page and under that module's permission: a catalog only links to it and never produces,
/// copies or calls it (rule T2).
/// </summary>
public sealed class ReportDefinition
{
    /// <summary>Report id: lowercase, dot-separated kebab-case segments, unique across modules, e.g. <c>weighing.records</c>.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Full UI string key (prefix included) of the report's name. The key must be in a string set the module registers
    /// itself; the platform checks it at startup.
    /// </summary>
    public required string NameKey { get; init; }

    /// <summary>Full UI string key of a one-sentence description, checked like <see cref="NameKey"/>.</summary>
    public required string DescriptionKey { get; init; }

    /// <summary>Who reads the report (at least one).</summary>
    public required ReportAudience Audience { get; init; }

    /// <summary>The forms the report comes in (at least one).</summary>
    public required ReportOutputs Outputs { get; init; }

    /// <summary>
    /// The web application path of the page that shows the report, starting with <c>/</c>, for example
    /// <c>/records</c> or <c>/m/formulation/batches</c>. An address inside the application only.
    /// </summary>
    public required string Link { get; init; }

    /// <summary>The permission the report's page needs, or null when it needs none. A users module checks it on that page.</summary>
    public Permission? Permission { get; init; }

    /// <summary>Whether the module can send the report automatically by itself (for example every evening).</summary>
    public bool CanSchedule { get; init; }
}

/// <summary>Who a report is for; a catalog filters by it ("Kim için").</summary>
[Flags]
public enum ReportAudience
{
    /// <summary>Nobody; not valid on a definition.</summary>
    None = 0,

    /// <summary>The owner or general manager.</summary>
    Owner = 1,

    /// <summary>Production management.</summary>
    Production = 2,

    /// <summary>Quality.</summary>
    Quality = 4,

    /// <summary>Accounting.</summary>
    Accounting = 8,

    /// <summary>An auditor or inspector.</summary>
    Auditor = 16,
}

/// <summary>The forms a report comes in.</summary>
[Flags]
public enum ReportOutputs
{
    /// <summary>None; not valid on a definition.</summary>
    None = 0,

    /// <summary>On the screen.</summary>
    Screen = 1,

    /// <summary>A PDF file (or the page printed to PDF).</summary>
    Pdf = 2,

    /// <summary>An Excel file.</summary>
    Excel = 4,
}
