namespace Vyxum.Platform.Abstractions;

/// <summary>Describes a module: identity, version, capabilities and published events.</summary>
public sealed class ModuleManifest
{
    /// <summary>Unique module id: lowercase, dot-separated kebab-case segments, e.g. <c>samples.orders</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Module version.</summary>
    public required Version Version { get; init; }

    /// <summary>Capabilities this module provides. Each capability has exactly one provider.</summary>
    public IReadOnlyList<Capability> Provides { get; init; } = [];

    /// <summary>Capabilities that must be provided by some module, otherwise startup fails.</summary>
    public IReadOnlyList<Capability> Requires { get; init; } = [];

    /// <summary>Capabilities the module uses when available; check them with <see cref="ICapabilityRegistry"/>.</summary>
    public IReadOnlyList<Capability> Optional { get; init; } = [];

    /// <summary>
    /// Integration event types (implementing <see cref="IIntegrationEvent"/>) this module publishes.
    /// An event name and version can be published by only one module.
    /// </summary>
    public IReadOnlyList<Type> Publishes { get; init; } = [];

    /// <summary>
    /// Whether the module works only with a license that names its <see cref="Id"/>. Default false (a free module).
    /// Without such a license the platform answers the module's endpoints with 403 and holds its integration event
    /// handlers; the module reads the details through <see cref="ILicenseState"/>.
    /// </summary>
    public bool RequiresLicense { get; init; }

    /// <summary>
    /// Full UI string key (prefix included, for example <c>weighing.ModuleName</c>) of the module's display name. The key
    /// must be in a string set the module registers itself (<c>AddUiStrings</c>); the platform checks at startup that it
    /// exists and serves the name of the current culture in <c>GET /api/ui/modules</c>. Null: the UI shows the id.
    /// </summary>
    public string? DisplayNameKey { get; init; }

    /// <summary>
    /// Permissions this module checks with <see cref="IAuthorization"/>. Ids are unique across modules; each name key must
    /// be in a string set the module registers. A users module lists them on its role screen.
    /// </summary>
    public IReadOnlyList<PermissionDefinition> Permissions { get; init; } = [];

    /// <summary>The module's screens, embedded in its assembly, or null when the module has none.</summary>
    public ModuleUi? Ui { get; init; }

    /// <summary>
    /// What the shell shows while the module is locked (a description and a sample image), or null for none. See
    /// <see cref="ModulePreview"/>.
    /// </summary>
    public ModulePreview? Preview { get; init; }
}
