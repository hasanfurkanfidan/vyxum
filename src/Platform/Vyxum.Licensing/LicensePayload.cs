namespace Vyxum.Licensing;

/// <summary>The kind of license, as written in the <c>type</c> field.</summary>
public enum LicenseType
{
    /// <summary>A time-limited trial issued by us.</summary>
    Trial,

    /// <summary>A subscription that ends on <see cref="LicensePayload.ValidUntil"/>.</summary>
    Subscription,

    /// <summary>A license without an end date.</summary>
    Perpetual,

    /// <summary>Not for resale (partners, demos).</summary>
    Nfr,
}

/// <summary>The party the license is issued to.</summary>
public sealed record Licensee
{
    /// <summary>Company or person name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional tax number.</summary>
    public string? TaxNumber { get; init; }
}

/// <summary>
/// The signed content of a <c>.vxlic</c> file. Serialized as camelCase JSON; unknown fields are ignored when reading.
/// </summary>
public sealed record LicensePayload
{
    /// <summary>The default number of grace days after <see cref="ValidUntil"/>.</summary>
    public const int DefaultGraceDays = 30;

    /// <summary>The largest accepted value of <see cref="GraceDays"/>.</summary>
    public const int MaxGraceDays = 3650;

    /// <summary>Unique identifier of this license.</summary>
    public required string LicenseId { get; init; }

    /// <summary>The kind of license.</summary>
    public required LicenseType Type { get; init; }

    /// <summary>The party the license is issued to.</summary>
    public required Licensee Licensee { get; init; }

    /// <summary>Licensed module ids (the module manifest <c>Id</c>, compared ordinally).</summary>
    public required IReadOnlyList<string> Modules { get; init; }

    /// <summary>Number of licensed stations (devices), at least 1.</summary>
    public required int Stations { get; init; }

    /// <summary>When the license was issued.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>First day the license works (inclusive, local date).</summary>
    public required DateOnly ValidFrom { get; init; }

    /// <summary>Last day the license works (inclusive, local date); <see langword="null"/> exactly for perpetual licenses.</summary>
    public DateOnly? ValidUntil { get; init; }

    /// <summary>Full days after <see cref="ValidUntil"/> during which the license still works with a warning.</summary>
    public int GraceDays { get; init; } = DefaultGraceDays;

    /// <summary>Optional end of the maintenance (updates) period.</summary>
    public DateOnly? MaintenanceUntil { get; init; }

    /// <summary>Optional reseller that sold the license.</summary>
    public string? ResellerId { get; init; }
}
