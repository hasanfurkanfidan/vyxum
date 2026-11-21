using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>Maps the agent hub and the device endpoints.</summary>
public static class DeviceEndpointExtensions
{
    /// <summary>Path of the device list endpoint.</summary>
    public const string DevicesPath = "/api/devices";

    /// <summary>JSON settings of the device endpoints and the agent hub: web defaults, enums as strings.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    /// <summary>
    /// Maps <see cref="AgentHub"/> at <see cref="AgentHubContract.Path"/>, guarded by
    /// <see cref="AgentConnectionGate"/>, and <c>GET /api/devices</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapVyxumDevices(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var gate = endpoints.ServiceProvider.GetRequiredService<AgentConnectionGate>();
        endpoints.MapHub<AgentHub>(AgentHubContract.Path).Add(builder =>
        {
            // Runs before negotiation and before the connection is accepted.
            var next = builder.RequestDelegate
                ?? throw new InvalidOperationException("The agent hub endpoint has no request delegate.");
            builder.RequestDelegate = context => gate.Admit(context) ? next(context) : Task.CompletedTask;
        });

        endpoints.MapGet(DevicesPath, (ILiveWeightSource source) => Results.Json(source.GetDevices(), JsonOptions));
        return endpoints;
    }

    internal static void ConfigureJson(JsonSerializerOptions options) =>
        options.Converters.Add(new JsonStringEnumConverter());

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ConfigureJson(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
