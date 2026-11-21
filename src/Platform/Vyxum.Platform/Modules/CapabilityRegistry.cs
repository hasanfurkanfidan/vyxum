using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary><see cref="ICapabilityRegistry"/> built from the manifests of the loaded modules.</summary>
public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly HashSet<Capability> _available;

    /// <summary>Creates a registry with every capability the given manifests provide.</summary>
    public CapabilityRegistry(IEnumerable<ModuleManifest> manifests)
    {
        _available = manifests.SelectMany(m => m.Provides).ToHashSet();
    }

    /// <inheritdoc />
    public bool IsAvailable(Capability capability) => _available.Contains(capability);
}
