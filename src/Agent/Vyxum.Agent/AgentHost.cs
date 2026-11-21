using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Vyxum.Agent.Configuration;

namespace Vyxum.Agent;

/// <summary>Builds the agent host: a console application that also runs as a Windows service.</summary>
public static class AgentHost
{
    /// <summary>The Windows service name.</summary>
    public const string ServiceName = "Vyxum Agent";

    /// <summary>
    /// The command-line option that names a settings file to use instead of <c>appsettings.json</c>,
    /// for example <c>--settings appsettings.Ind231.example.json</c>.
    /// </summary>
    public const string SettingsFileOption = "settings";

    /// <summary>
    /// Creates the builder used by the executable. Settings come from <c>appsettings.json</c> next to the executable
    /// (or the file named by <c>--settings</c>), environment variables and the command line.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args) =>
        CreateBuilder(new HostApplicationBuilderSettings
        {
            Args = args,

            // A service starts in the system folder; the settings file lives next to the executable.
            ContentRootPath = AppContext.BaseDirectory,
        });

    /// <summary>Creates the builder from host settings, for example with defaults disabled and in-memory configuration in tests.</summary>
    public static HostApplicationBuilder CreateBuilder(HostApplicationBuilderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var builder = Host.CreateApplicationBuilder(settings);
        UseSettingsFile(builder.Configuration);
        builder.Services.AddWindowsService(options => options.ServiceName = ServiceName);
        builder.Services.AddVyxumAgent(builder.Configuration);
        return builder;
    }

    /// <summary>
    /// Registers the agent: settings from <c>Vyxum:Agent</c>, the device fleet, the device worker and the hub worker.
    /// Configure <see cref="AgentHubClientOptions"/> to change how the hub connection is made.
    /// </summary>
    public static IServiceCollection AddVyxumAgent(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AgentSettings>().Configure(settings => AgentConfiguration.BindInto(configuration, settings));
        services.AddOptions<AgentHubClientOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DeviceFleet>();
        services.AddHostedService<DeviceWorker>();
        services.AddHostedService<HubWorker>();
        return services;
    }

    /// <summary>Replaces <c>appsettings.json</c> with the file named by <c>--settings</c>, when given.</summary>
    private static void UseSettingsFile(ConfigurationManager configuration)
    {
        var file = configuration[SettingsFileOption];
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        var path = Path.GetFullPath(file);
        if (!File.Exists(path))
        {
            throw new AgentConfigurationException($"--{SettingsFileOption}: the file '{path}' does not exist.");
        }

        var replacement = new ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: false).Sources[0];
        IList<IConfigurationSource> sources = ((IConfigurationBuilder)configuration).Sources;
        var index = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource { Path: "appsettings.json" })
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            sources[index] = replacement;
        }
        else
        {
            // Defaults disabled: add it before the command line so that command-line values still win.
            sources.Insert(0, replacement);
        }
    }
}
