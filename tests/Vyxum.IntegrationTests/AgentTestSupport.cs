using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Vyxum.Agent;
using Vyxum.Devices;

namespace Vyxum.IntegrationTests;

/// <summary>A simulated device for an in-process agent: MT-SICS by default, or a listen-only Toledo Continuous indicator.</summary>
internal sealed record SimulatedDevice(string Id, decimal Weight, string Unit, WeightState State = WeightState.Stable, bool Toledo = false);

/// <summary>Builds in-process agents and waits for conditions without fixed sleeps.</summary>
internal static class AgentTestSupport
{
    public const string AgentName = "test-agent";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>Agent settings with short intervals so that tests run fast.</summary>
    public static Dictionary<string, string?> AgentConfig(string serverUrl, string? key, params SimulatedDevice[] devices)
    {
        var values = new Dictionary<string, string?>
        {
            ["Vyxum:Agent:ServerUrl"] = serverUrl,
            ["Vyxum:Agent:Key"] = key,
            ["Vyxum:Agent:AgentName"] = AgentName,
            ["Vyxum:Agent:HeartbeatInterval"] = "00:00:00.200",
            ["Vyxum:Agent:ReconnectDelay"] = "00:00:00.200",
            ["Vyxum:Agent:MaxReconnectDelay"] = "00:00:01",
        };
        for (var i = 0; i < devices.Length; i++)
        {
            var prefix = $"Vyxum:Agent:Devices:{i}:";
            values[prefix + "Id"] = devices[i].Id;
            values[prefix + "Transport:Type"] = "simulator";
            values[prefix + "Transport:Simulator:Weight"] = devices[i].Weight.ToString(CultureInfo.InvariantCulture);
            values[prefix + "Transport:Simulator:Unit"] = devices[i].Unit;
            values[prefix + "Transport:Simulator:State"] = devices[i].State.ToString();
            if (devices[i].Toledo)
            {
                values[prefix + "Transport:Simulator:Kind"] = "ToledoContinuous";
                values[prefix + "Transport:Simulator:Interval"] = "00:00:00.020";
                values[prefix + "Protocol:Type"] = "toledo-continuous";
                continue;
            }

            values[prefix + "Transport:Simulator:Kind"] = "MtSics";
            values[prefix + "Protocol:Type"] = "mt-sics";
            values[prefix + "Protocol:Command"] = "SI";
            values[prefix + "Protocol:PollInterval"] = "00:00:00.050";
            values[prefix + "Protocol:ResponseTimeout"] = "00:00:00.500";
        }

        return values;
    }

    /// <summary>Builds an agent host from in-memory settings, the way the executable does but without files.</summary>
    public static IHost BuildAgent(IDictionary<string, string?> settings, Action<HttpConnectionOptions>? configureConnection = null)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(settings);
        var builder = AgentHost.CreateBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            Configuration = configuration,
        });
        if (configureConnection is not null)
        {
            builder.Services.Configure<AgentHubClientOptions>(o => o.ConfigureHttpConnection = configureConnection);
        }

        return builder.Build();
    }

    /// <summary>Polls <paramref name="condition"/> until it is true; fails the test after <see cref="Timeout"/>.</summary>
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            if (await condition())
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new ShouldAssertException($"Timed out after {Timeout.TotalSeconds} s waiting until {what}.");
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    /// <summary>The device from <c>GET /api/devices</c>, or null when it is not listed.</summary>
    public static async Task<JsonElement?> GetDeviceAsync(HttpClient client, string deviceId, CancellationToken cancellationToken)
    {
        var body = await client.GetStringAsync("/api/devices", cancellationToken);
        using var json = JsonDocument.Parse(body);
        foreach (var device in json.RootElement.EnumerateArray())
        {
            if (device.GetProperty("deviceId").GetString() == deviceId)
            {
                return device.Clone();
            }
        }

        return null;
    }

    public static string? State(this JsonElement? device) => device?.GetProperty("state").GetString();

    public static string? LastError(this JsonElement? device) => device?.GetProperty("lastError").GetString();

    public static bool HasWeight(this JsonElement? device) =>
        device is { } d && d.GetProperty("lastWeight").ValueKind == JsonValueKind.Object;
}
