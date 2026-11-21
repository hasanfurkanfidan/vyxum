using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform.Tests.Modules;

/// <summary>A module whose manifest and registration are supplied by the test.</summary>
internal sealed class TestModule(ModuleManifest manifest, Action<IServiceCollection, ModuleContext>? configure = null) : IModule
{
    public ModuleManifest Manifest { get; } = manifest;

    public void ConfigureServices(IServiceCollection services, ModuleContext context) => configure?.Invoke(services, context);

    public static TestModule Create(
        string id,
        Capability[]? provides = null,
        Capability[]? requires = null,
        Capability[]? optional = null,
        Type[]? publishes = null) =>
        new(new ModuleManifest
        {
            Id = id,
            Version = new Version(1, 0),
            Provides = provides ?? [],
            Requires = requires ?? [],
            Optional = optional ?? [],
            Publishes = publishes ?? [],
        });
}
