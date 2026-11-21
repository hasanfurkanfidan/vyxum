namespace Vyxum.Platform.Abstractions;

/// <summary>
/// A named feature a module provides or needs, e.g. <c>weighing.records</c>.
/// Ids are lowercase, dot-separated kebab-case segments.
/// </summary>
/// <param name="Id">The capability id.</param>
public readonly record struct Capability(string Id)
{
    /// <summary>Creates a capability from its id.</summary>
    public static implicit operator Capability(string id) => new(id);

    /// <inheritdoc />
    public override string ToString() => Id ?? string.Empty;
}
