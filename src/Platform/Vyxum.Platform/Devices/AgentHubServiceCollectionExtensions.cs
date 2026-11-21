using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>Registers the services of the agent hub.</summary>
public static class AgentHubServiceCollectionExtensions
{
    /// <summary>
    /// Registers SignalR (enums as strings), <see cref="AgentOptions"/>, <see cref="AgentConnectionGate"/>, the device settings (<see cref="DeviceConfigService"/>) and
    /// <see cref="Abstractions.IDeviceCommands"/> (sent through the hub).
    /// Call after <see cref="PlatformServiceCollectionExtensions.AddVyxumPlatform(IServiceCollection, IConfiguration, IEnumerable{Abstractions.IModule})"/>
    /// in a web host, then map the hub with <see cref="DeviceEndpointExtensions.MapVyxumDevices"/>.
    /// </summary>
    public static IServiceCollection AddVyxumAgentHub(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.AddSingleton<AgentConnectionGate>();
        services.AddSingleton<Abstractions.IDeviceCommands, AgentDeviceCommands>();

        // Device settings (decision V-39): /api/devices/config, hub GetDevices and DevicesChanged.
        services.AddSingleton<AgentDeviceAssignments>();

        // Device discovery (decision V-40): /api/devices/discover, hub Discover and ReportDiscoveryProgress.
        services.AddSingleton<DiscoveryJobs>();
        services.AddScoped(provider => new DeviceConfigService(
            provider.GetRequiredService<PlatformDbContext>(),
            provider.GetRequiredService<LiveWeightStore>(),
            provider.GetRequiredService<AgentDeviceAssignments>(),
            provider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<AgentHub>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DeviceConfigService>>()));
        services.AddHostedService<DeviceConfigLoader>();
        services.AddUiStrings(DeviceStrings.KeyPrefix, DeviceStrings.ResourceManager);
        services.AddSignalR().AddJsonProtocol(options => DeviceEndpointExtensions.ConfigureJson(options.PayloadSerializerOptions));
        return services;
    }
}
