using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Vyxum.Platform;

/// <summary>Maps the HTTP endpoints of the loaded modules.</summary>
public static class ModuleEndpointExtensions
{
    /// <summary>Calls <see cref="Abstractions.IModule.MapEndpoints"/> on every loaded module.</summary>
    public static IEndpointRouteBuilder MapVyxumModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        foreach (var module in endpoints.ServiceProvider.GetRequiredService<ModuleCatalog>().Modules)
        {
            module.Module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
