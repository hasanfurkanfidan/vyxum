using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>A loaded module together with what it registered during <see cref="IModule.ConfigureServices"/>.</summary>
public sealed class ModuleDescriptor
{
    /// <summary>Creates a descriptor for <paramref name="module"/>.</summary>
    public ModuleDescriptor(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Module = module;
        Manifest = module.Manifest;
    }

    /// <summary>The module instance.</summary>
    public IModule Module { get; }

    /// <summary>The module's manifest (read once).</summary>
    public ModuleManifest Manifest { get; }
}
