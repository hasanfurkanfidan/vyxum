using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.IntegrationTests;

/// <summary>Requires a capability that no module provides; used to prove the host refuses to start.</summary>
public sealed class MissingCapabilityModule : IModule
{
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "tests.needs-missing",
        Version = new Version(1, 0),
        Requires = ["tests.missing-capability"],
    };

    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
    }
}
