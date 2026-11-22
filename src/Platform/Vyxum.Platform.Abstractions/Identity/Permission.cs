namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Something a person may be allowed to do, e.g. <c>weighing.cancel</c>. Ids are lowercase, dot-separated kebab-case
/// segments, like capability ids. A module declares its permissions in <see cref="ModuleManifest.Permissions"/> and
/// checks them with <see cref="IAuthorization"/>.
/// </summary>
/// <param name="Id">The permission id.</param>
public readonly record struct Permission(string Id)
{
    /// <summary>Creates a permission from its id.</summary>
    public static implicit operator Permission(string id) => new(id);

    /// <inheritdoc />
    public override string ToString() => Id ?? string.Empty;
}
