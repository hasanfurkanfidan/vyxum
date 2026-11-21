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

    /// <summary>Route of the zero command; <c>{id}</c> is the device id.</summary>
    public const string ZeroPath = "/api/devices/{id}/zero";

    /// <summary>Path of the device settings list; <c>/api/devices/config/{id}</c> addresses one device.</summary>
    public const string ConfigPath = "/api/devices/config";

    /// <summary>Path of the device discovery endpoint; <c>/api/devices/discover/{id}</c> addresses one job.</summary>
    public const string DiscoverPath = "/api/devices/discover";

    /// <summary>JSON settings of the device endpoints and the agent hub: web defaults, enums as strings.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    /// <summary>
    /// Maps <see cref="AgentHub"/> at <see cref="AgentHubContract.Path"/>, guarded by
    /// <see cref="AgentConnectionGate"/>, <c>GET /api/devices</c>, <c>GET /api/devices/{id}/live</c> and
    /// <c>POST /api/devices/{id}/zero</c>, and the device settings API under <see cref="ConfigPath"/>.
    /// </summary>
    /// <remarks>
    /// The live endpoint is a Server-Sent Events stream (<c>text/event-stream</c>). Every event is an
    /// unnamed <c>data:</c> line with one <see cref="LiveDevice"/> as JSON (same settings as
    /// <c>/api/devices</c>): one at once, then one whenever the device's state or weight changes
    /// (see <see cref="ILiveWeightSource.WatchDeviceAsync"/>). An unknown device id is not an error:
    /// the stream starts with a waiting snapshot (state <c>Disconnected</c>, <c>agentName</c> null) so
    /// that a browser can open it before the agent connects, and continues once the agent reports the device.
    /// <para>
    /// The zero endpoint sends the zero command once per request (decision V-29) and always answers 200 with
    /// <c>{ "result": "Done" | "NotSupported" | "Rejected" | "Unavailable", "detail"?: "..." }</c>
    /// (<see cref="DeviceCommandResult"/>); <c>detail</c> is English text for logs and is left out when empty.
    /// </para>
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
        endpoints.MapPost(ZeroPath, async (string id, IDeviceCommands commands, CancellationToken cancellationToken) =>
            Results.Json(await commands.ZeroAsync(id, cancellationToken), JsonOptions));
        MapConfig(endpoints);
        MapDiscover(endpoints);
        return endpoints;
    }

    /// <summary>
    /// Maps the device settings API (decision V-39) under <see cref="ConfigPath"/>: <c>GET</c> (list, ordered by id),
    /// <c>GET {id}</c>, <c>POST</c> (201 with the device), <c>PUT {id}</c> (200 with the device) and <c>DELETE {id}</c>
    /// (204). An unknown id is 404; invalid input is a 400 validation problem whose messages are UI string keys
    /// (<c>devices.*</c>). Like the product cards, the free edition has no login (decision V-03), so the API is open
    /// to every caller that can reach the web application.
    /// </summary>
    private static void MapConfig(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ConfigPath);
        group.MapGet("/", async (DeviceConfigService devices, CancellationToken ct) =>
            Results.Json(await devices.ListAsync(ct), JsonOptions));
        group.MapGet("/{id}", async (string id, DeviceConfigService devices, CancellationToken ct) =>
            await devices.GetAsync(id, ct) is { } device ? Results.Json(device, JsonOptions) : Results.NotFound());
        group.MapPost("/", async (DeviceConfigInput input, DeviceConfigService devices, HttpContext context, CancellationToken ct) =>
        {
            var result = await devices.CreateAsync(input, ct);
            if (result.Device is not { } device)
            {
                return ToHttp(result);
            }

            context.Response.Headers.Location = ConfigPath + "/" + device.Id;
            return Results.Json(device, JsonOptions, statusCode: StatusCodes.Status201Created);
        });
        group.MapPut("/{id}", async (string id, DeviceConfigInput input, DeviceConfigService devices, CancellationToken ct) =>
            ToHttp(await devices.UpdateAsync(id, input, ct)));
        group.MapDelete("/{id}", async (string id, DeviceConfigService devices, CancellationToken ct) =>
            await devices.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }

    /// <summary>
    /// Maps device discovery (decision V-40). <c>POST</c> with <c>{ "agentName"?: string, "tcp"?: { "host", "port" } }</c>
    /// (body optional) starts a job and answers 202 with <c>{ "id" }</c>; 409 <c>{ "code": "devices.DiscoveryRunning" }</c>
    /// when the agent already scans; 503 <c>{ "code": "devices.NoAgent" }</c> when no agent is connected; 400 for an
    /// invalid network address (<c>tcp.host</c>, <c>tcp.port</c>). <c>GET {id}</c> returns the job
    /// (<see cref="DiscoveryJobView"/>) or 404. Open to every caller, like the device settings (decision V-03).
    /// </summary>
    private static void MapDiscover(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(DiscoverPath, (DiscoveryStartInput? input, DiscoveryJobs jobs, HttpContext context) =>
        {
            DiscoveryTcpTarget? tcp = null;
            if (input?.Tcp is { } target)
            {
                var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
                if (string.IsNullOrWhiteSpace(target.Host))
                {
                    errors["tcp.host"] = [DeviceStrings.Code("HostRequired")];
                }

                if (target.Port is not (>= 1 and <= 65535))
                {
                    errors["tcp.port"] = [DeviceStrings.Code("PortInvalid")];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                tcp = new DiscoveryTcpTarget(target.Host!.Trim(), target.Port!.Value);
            }

            var started = jobs.Start(input?.AgentName, tcp);
            if (started.Id is not { } id)
            {
                var status = started.Code == DeviceStrings.Code("NoAgent") ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict;
                return Results.Json(new { code = started.Code }, JsonOptions, statusCode: status);
            }

            context.Response.Headers.Location = DiscoverPath + "/" + id;
            return Results.Json(new { id }, JsonOptions, statusCode: StatusCodes.Status202Accepted);
        });
        endpoints.MapGet(DiscoverPath + "/{id}", (string id, DiscoveryJobs jobs) =>
            jobs.Get(id) is { } job ? Results.Json(job, JsonOptions) : Results.NotFound());
    }

    private static IResult ToHttp(DeviceConfigResult result) =>
        result.NotFound ? Results.NotFound()
        : result.Errors is { } errors ? Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value))
        : Results.Json(result.Device, JsonOptions);

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

/// <summary>The body of <c>POST /api/devices/discover</c>.</summary>
/// <param name="AgentName">The agent that should scan; null for the default agent.</param>
/// <param name="Tcp">A network address to probe instead of the serial ports; null to scan the serial ports.</param>
public sealed record DiscoveryStartInput(string? AgentName, DiscoveryTcpInput? Tcp);

/// <summary>A network address in <see cref="DiscoveryStartInput"/>.</summary>
/// <param name="Host">Host name or IP address.</param>
/// <param name="Port">TCP port, 1 to 65535.</param>
public sealed record DiscoveryTcpInput(string? Host, int? Port);
