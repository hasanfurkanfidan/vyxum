using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>The permissions of the loaded modules, read from their manifests once at startup.</summary>
internal sealed class PermissionCatalog : IPermissionCatalog
{
    private readonly Dictionary<string, DeclaredPermission> _byId;

    public PermissionCatalog(ModuleCatalog modules)
    {
        Permissions = modules.Modules
            .OrderBy(m => m.Manifest.Id, StringComparer.Ordinal)
            .SelectMany(m => m.Manifest.Permissions.Select(p => new DeclaredPermission(m.Manifest.Id, p)))
            .ToList();
        // The validator rejects duplicate ids at startup; keep the first in case of a test catalog.
        _byId = new Dictionary<string, DeclaredPermission>(StringComparer.Ordinal);
        foreach (var permission in Permissions)
        {
            _byId.TryAdd(permission.Definition.Id.Id, permission);
        }
    }

    public IReadOnlyList<DeclaredPermission> Permissions { get; }

    public DeclaredPermission? Find(Permission permission) =>
        permission.Id is not null && _byId.TryGetValue(permission.Id, out var found) ? found : null;
}
