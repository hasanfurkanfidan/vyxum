using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Vyxum.Platform;

/// <summary>Registers the services of the agent hub.</summary>
public static class AgentHubServiceCollectionExtensions
{
    /// <summary>
    /// Registers SignalR (enums as strings), <see cref="AgentOptions"/> and <see cref="AgentConnectionGate"/>.
    /// Call after <see cref="PlatformServiceCollectionExtensions.AddVyxumPlatform(IServiceCollection, IConfiguration, IEnumerable{Abstractions.IModule})"/>
    /// in a web host, then map the hub with <see cref="DeviceEndpointExtensions.MapVyxumDevices"/>.
    /// </summary>
    public static IServiceCollection AddVyxumAgentHub(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.AddSingleton<AgentConnectionGate>();
        services.AddSignalR().AddJsonProtocol(options => DeviceEndpointExtensions.ConfigureJson(options.PayloadSerializerOptions));
        return services;
    }
}
