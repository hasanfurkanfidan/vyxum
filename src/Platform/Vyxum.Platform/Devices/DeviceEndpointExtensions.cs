using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>Maps the agent hub and the device endpoints.</summary>
public static class DeviceEndpointExtensions
{
    /// <summary>Path of the device list endpoint.</summary>
    public const string DevicesPath = "/api/devices";

    /// <summary>Route of the live device stream; <c>{id}</c> is the device id.</summary>
    public const string LivePath = "/api/devices/{id}/live";

    /// <summary>JSON settings of the device endpoints and the agent hub: web defaults, enums as strings.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    /// <summary>
    /// Maps <see cref="AgentHub"/> at <see cref="AgentHubContract.Path"/>, guarded by
    /// <see cref="AgentConnectionGate"/>, <c>GET /api/devices</c> and <c>GET /api/devices/{id}/live</c>.
    /// </summary>
    /// <remarks>
    /// The live endpoint is a Server-Sent Events stream (<c>text/event-stream</c>). Every event is an
    /// unnamed <c>data:</c> line with one <see cref="LiveDevice"/> as JSON (same settings as
    /// <c>/api/devices</c>): one at once, then one whenever the device's state or weight changes
    /// (see <see cref="ILiveWeightSource.WatchDeviceAsync"/>). An unknown device id is not an error:
    /// the stream starts with a waiting snapshot (state <c>Disconnected</c>, <c>agentName</c> null) so
    /// that a browser can open it before the agent connects, and continues once the agent reports the device.
    /// </remarks>
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
        endpoints.MapGet(LivePath, StreamLiveAsync);
        return endpoints;
    }

    private static async Task StreamLiveAsync(string id, ILiveWeightSource source, HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await response.StartAsync(cancellationToken);
            await foreach (var device in source.WatchDeviceAsync(id, cancellationToken))
            {
                var json = JsonSerializer.Serialize(device, JsonOptions);
                await response.WriteAsync("data: " + json + "\n\n", Encoding.UTF8, cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client closed the stream.
        }
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
