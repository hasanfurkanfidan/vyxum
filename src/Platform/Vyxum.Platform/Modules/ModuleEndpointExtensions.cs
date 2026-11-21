using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Vyxum.Platform;

/// <summary>Maps the HTTP endpoints of the loaded modules.</summary>
public static class ModuleEndpointExtensions
{
    /// <summary>
    /// Calls <see cref="Abstractions.IModule.MapEndpoints"/> on every loaded module, each inside its own route group
    /// without a prefix. The group of a module that requires a license gets the license gate (see <see cref="LicenseGate"/>).
    /// </summary>
    public static IEndpointRouteBuilder MapVyxumModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        foreach (var module in endpoints.ServiceProvider.GetRequiredService<ModuleCatalog>().Modules)
        {
            var group = endpoints.MapGroup(string.Empty);
            if (module.Manifest.RequiresLicense)
            {
                LicenseGate.Apply(group, module.Manifest.Id);
            }

            module.Module.MapEndpoints(group);
        }

        return endpoints;
    }
}
