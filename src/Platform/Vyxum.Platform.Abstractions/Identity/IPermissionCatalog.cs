namespace Vyxum.Platform.Abstractions;

/// <summary>The permissions declared by the loaded modules (<see cref="ModuleManifest.Permissions"/>).</summary>
public interface IPermissionCatalog
{
    /// <summary>Every declared permission, ordered by module id and then in declaration order.</summary>
    IReadOnlyList<DeclaredPermission> Permissions { get; }

    /// <summary>The declaration of <paramref name="permission"/>, or null when no loaded module declares it.</summary>
    DeclaredPermission? Find(Permission permission);
}

/// <summary>A permission and the module that declares it.</summary>
/// <param name="ModuleId">Manifest id of the declaring module.</param>
/// <param name="Definition">The declaration.</param>
public sealed record DeclaredPermission(string ModuleId, PermissionDefinition Definition);
