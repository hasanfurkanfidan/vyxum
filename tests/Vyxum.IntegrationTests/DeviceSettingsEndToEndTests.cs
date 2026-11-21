using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Vyxum.Agent;
using Vyxum.Platform;
using Xunit;
using static Vyxum.IntegrationTests.AgentTestSupport;

namespace Vyxum.IntegrationTests;

/// <summary>
/// Devices configured in the web application are run by a real in-process agent (decision V-39): added, changed and
/// removed while both keep running.
/// </summary>
public sealed class DeviceSettingsEndToEndTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-devset-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly; the temp folder is disposable anyway.
        }
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory));

    private static IHost BuildAgent(TestServer server, params SimulatedDevice[] localDevices) =>
        AgentTestSupport.BuildAgent(
            AgentConfig(server.BaseAddress.ToString(), key: null, localDevices),
            options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            });

    private static async Task SendAsync(HttpClient client, HttpMethod method, string path, string? json, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
    }

    private static Task AddAsync(HttpClient client, string json) =>
        SendAsync(client, HttpMethod.Post, DeviceEndpointExtensions.ConfigPath, json, HttpStatusCode.Created);

    private static Task ChangeAsync(HttpClient client, string id, string json) =>
        SendAsync(client, HttpMethod.Put, DeviceEndpointExtensions.ConfigPath + "/" + id, json, HttpStatusCode.OK);

    private static async Task WaitForWeightAsync(HttpClient client, string deviceId, decimal value, string unit)
    {
        await WaitUntilAsync(
            async () =>
            {
                var device = await GetDeviceAsync(client, deviceId, Ct);
                if (!device.HasWeight() || device.State() != "Connected")
                {
                    return false;
                }

                var weight = device!.Value.GetProperty("lastWeight");
                return weight.GetProperty("value").GetDecimal() == value && weight.GetProperty("unit").GetString() == unit;
            },
            $"{deviceId} shows {value} {unit}",
            Ct);
    }

    [Fact]
    public async Task Device_added_in_the_web_is_read_then_changed_and_removed_live()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server);
        await agent.StartAsync(Ct);
        var fleet = agent.Services.GetRequiredService<DeviceFleet>();
        fleet.UsesLocalDevices.ShouldBeFalse();
        fleet.Devices.ShouldBeEmpty();

        await AddAsync(client, DeviceConfigApiTests.Simulator("sim-1", 572m, "g"));
        await WaitForWeightAsync(client, "sim-1", 572m, "g");
        var device = (await GetDeviceAsync(client, "sim-1", Ct))!.Value;
        device.GetProperty("agentName").GetString().ShouldBe(AgentName);
        device.GetProperty("supportedCommands")[0].GetString().ShouldBe("Zero");

        // A changed device is rebuilt with the new settings.
        var firstRunner = fleet.GetRunner("sim-1");
        await ChangeAsync(client, "sim-1", DeviceConfigApiTests.Simulator("sim-1", 1.250m, "kg"));
        await WaitForWeightAsync(client, "sim-1", 1.250m, "kg");
        fleet.GetRunner("sim-1").ShouldNotBeSameAs(firstRunner);

        // A removed device stops and disappears from the device list.
        await SendAsync(client, HttpMethod.Delete, DeviceEndpointExtensions.ConfigPath + "/sim-1", null, HttpStatusCode.NoContent);
        await WaitUntilAsync(async () => await GetDeviceAsync(client, "sim-1", Ct) is null, "the device disappears", Ct);
        await WaitUntilAsync(() => Task.FromResult(fleet.Devices.Count == 0), "the agent stops the device", Ct);

        await agent.StopAsync(Ct);
    }

    [Fact]
    public async Task Unchanged_device_keeps_running_when_another_device_or_only_its_name_changes()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server);
        await agent.StartAsync(Ct);
        var fleet = agent.Services.GetRequiredService<DeviceFleet>();

        await AddAsync(client, DeviceConfigApiTests.Simulator("sim-a", 100m, "g"));
        await AddAsync(client, DeviceConfigApiTests.Simulator("sim-b", 200m, "g"));
        await WaitForWeightAsync(client, "sim-a", 100m, "g");
        await WaitForWeightAsync(client, "sim-b", 200m, "g");

        var runner = fleet.GetRunner("sim-a");
        var simulator = fleet.Devices.Single(d => d.Id == "sim-a").Simulator.ShouldNotBeNull();
        var opens = simulator.OpenCount;

        await ChangeAsync(client, "sim-b", DeviceConfigApiTests.Simulator("sim-b", 250m, "g"));
        await WaitForWeightAsync(client, "sim-b", 250m, "g");

        // Name, capacity and division are for screens; the agent's settings stay the same.
        var renamed = DeviceConfigApiTests.Simulator("sim-a", 100m, "g").Replace("\"id\": \"sim-a\",", "\"id\": \"sim-a\", \"name\": \"Scale A\", \"capacity\": \"6\", \"division\": \"0.002\", \"unit\": \"kg\",", StringComparison.Ordinal);
        await ChangeAsync(client, "sim-a", renamed);
        await WaitUntilAsync(async () => (await GetDeviceAsync(client, "sim-a", Ct))!.Value.GetProperty("name").GetString() == "Scale A", "the new name is shown", Ct);
        await AddAsync(client, DeviceConfigApiTests.Simulator("sim-c", 300m, "g"));
        await WaitForWeightAsync(client, "sim-c", 300m, "g");

        fleet.GetRunner("sim-a").ShouldBeSameAs(runner);
        simulator.OpenCount.ShouldBe(opens);
        var a = (await GetDeviceAsync(client, "sim-a", Ct))!.Value;
        a.GetProperty("state").GetString().ShouldBe("Connected");
        a.GetProperty("capacity").GetDecimal().ShouldBe(6m);
        a.GetProperty("division").GetDecimal().ShouldBe(0.002m);
        a.GetProperty("unit").GetString().ShouldBe("kg");

        await agent.StopAsync(Ct);
    }

    [Fact]
    public async Task Invalid_device_is_reported_as_error_and_the_others_still_run()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server);
        await agent.StartAsync(Ct);

        // The platform does not know protocol names; the agent rejects this one.
        var invalid = DeviceConfigApiTests.Simulator("bad-1").Replace("\"type\": \"mt-sics\"", "\"type\": \"no-such-protocol\"", StringComparison.Ordinal);
        await AddAsync(client, invalid);
        await AddAsync(client, DeviceConfigApiTests.Simulator("good-1", 572m, "g"));

        await WaitForWeightAsync(client, "good-1", 572m, "g");
        string? error = null;
        await WaitUntilAsync(
            async () =>
            {
                var device = await GetDeviceAsync(client, "bad-1", Ct);
                error = device.LastError();
                return device.State() == "Error";
            },
            "the invalid device is shown with an error",
            Ct);
        error.ShouldNotBeNull();
        error.ShouldContain("no-such-protocol");

        // Fixed in the web: the device starts.
        await ChangeAsync(client, "bad-1", DeviceConfigApiTests.Simulator("bad-1", 9m, "g"));
        await WaitForWeightAsync(client, "bad-1", 9m, "g");
        (await GetDeviceAsync(client, "bad-1", Ct)).LastError().ShouldBeNull();

        await agent.StopAsync(Ct);
    }

    [Fact]
    public async Task Device_assigned_to_another_computer_is_not_run()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server);
        await agent.StartAsync(Ct);

        var other = DeviceConfigApiTests.Simulator("other-1").Replace("\"id\": \"other-1\",", "\"id\": \"other-1\", \"agentName\": \"OTHER-PC\",", StringComparison.Ordinal);
        var mine = DeviceConfigApiTests.Simulator("mine-1").Replace("\"id\": \"mine-1\",", $"\"id\": \"mine-1\", \"agentName\": \"{AgentName.ToUpperInvariant()}\",", StringComparison.Ordinal);
        await AddAsync(client, other);
        await AddAsync(client, mine);

        await WaitForWeightAsync(client, "mine-1", 572m, "g");
        var device = await GetDeviceAsync(client, "other-1", Ct);
        device.State().ShouldBe("Disconnected");
        device!.Value.GetProperty("agentName").GetString().ShouldBeNull();
        agent.Services.GetRequiredService<DeviceFleet>().DeviceIds.ShouldBe(["mine-1"]);

        await agent.StopAsync(Ct);
    }

    [Fact]
    public async Task Agent_with_local_devices_ignores_the_devices_from_the_web()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await AddAsync(client, DeviceConfigApiTests.Simulator("web-1"));
        using var agent = BuildAgent(factory.Server, new SimulatedDevice("local-1", 572m, "g"));
        await agent.StartAsync(Ct);
        var fleet = agent.Services.GetRequiredService<DeviceFleet>();
        fleet.UsesLocalDevices.ShouldBeTrue();

        await WaitForWeightAsync(client, "local-1", 572m, "g");
        await ChangeAsync(client, "web-1", DeviceConfigApiTests.Simulator("web-1", 1m, "g"));
        await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);

        // The agent never asked for its devices, so it does not take part in the device settings.
        factory.Services.GetRequiredService<AgentDeviceAssignments>().Connections.ShouldBeEmpty();
        fleet.DeviceIds.ShouldBe(["local-1"]);
        var web = await GetDeviceAsync(client, "web-1", Ct);
        web.State().ShouldBe("Disconnected");
        web.HasWeight().ShouldBeFalse();

        await agent.StopAsync(Ct);
    }
}
