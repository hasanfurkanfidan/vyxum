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
}
