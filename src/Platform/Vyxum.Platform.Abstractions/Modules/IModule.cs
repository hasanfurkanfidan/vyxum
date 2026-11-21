using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Entry point of a Vyxum module. The platform discovers implementations, validates their
/// manifests at startup and lets each module register its services and endpoints.
/// Implementations need a public parameterless constructor.
/// </summary>
public interface IModule
{
    /// <summary>Identity, version and capabilities of the module.</summary>
    ModuleManifest Manifest { get; }

    /// <summary>Registers the module's services, its DbContext and its event handlers.</summary>
    void ConfigureServices(IServiceCollection services, ModuleContext context);

    /// <summary>Maps the module's HTTP endpoints. Optional.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
