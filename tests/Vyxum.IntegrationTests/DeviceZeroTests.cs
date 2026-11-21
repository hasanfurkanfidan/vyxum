using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Vyxum.Agent;
using Vyxum.Devices.Simulator;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;
using static Vyxum.IntegrationTests.AgentTestSupport;

namespace Vyxum.IntegrationTests;

/// <summary>The zero command end to end: endpoint, platform, hub client result, agent, protocol and simulator (decision V-29).</summary>
public sealed class DeviceZeroTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-zero-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly; the temp folder is disposable anyway.
        }
    }

    private WebApplicationFactory<Program> CreateFactory(TimeSpan? commandTimeout = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory);
            if (commandTimeout is { } timeout)
            {
                builder.UseSetting("Vyxum:Agents:CommandTimeout", timeout.ToString("c"));
            }
        });

    private static IHost BuildAgent(TestServer server, params SimulatedDevice[] devices) =>
        AgentTestSupport.BuildAgent(
            AgentConfig(server.BaseAddress.ToString(), key: null, devices),
            options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            });

    private static string ZeroPath(string deviceId) => DeviceEndpointExtensions.ZeroPath.Replace("{id}", deviceId, StringComparison.Ordinal);

    private static async Task<(string Body, JsonElement Json)> PostZeroAsync(HttpClient client, string deviceId, CancellationToken ct)
    {
        using var response = await client.PostAsync(ZeroPath(deviceId), content: null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var json = JsonDocument.Parse(body);
        return (body, json.RootElement.Clone());
    }

    [Fact]
    public async Task Zero_sends_Z_once_and_the_next_reading_is_zero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server, new SimulatedDevice("scale-1", 572m, "g"));
        await agent.StartAsync(ct);
        var source = factory.Services.GetRequiredService<ILiveWeightSource>();
        await WaitUntilAsync(() => Task.FromResult(source.GetLatest("scale-1")?.Value == 572m), "a weight arrives", ct);

        var device = (await GetDeviceAsync(client, "scale-1", ct))!.Value;
        device.GetProperty("supportedCommands").EnumerateArray().Select(c => c.GetString()).ShouldBe(["Zero"]);

        var (body, json) = await PostZeroAsync(client, "scale-1", ct);

        body.ShouldBe("{\"result\":\"Done\"}");
        json.GetProperty("result").GetString().ShouldBe("Done");
        await WaitUntilAsync(() => Task.FromResult(source.GetLatest("scale-1")?.Value == 0m), "the weight is zero", ct);

        var simulator = (MtSicsScaleSimulator)GetSimulatedDevice(agent);
        simulator.ZeroCommandCount.ShouldBe(1);
        simulator.ForbiddenCommands.ShouldBeEmpty();
        simulator.ReceivedCommands.Distinct().Order(StringComparer.Ordinal).ShouldBe(["SI", "Z"]);

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Zero_on_a_listen_only_device_is_not_supported()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server, new SimulatedDevice("toledo-1", 5m, "kg", Toledo: true));
        await agent.StartAsync(ct);
        await WaitUntilAsync(async () => (await GetDeviceAsync(client, "toledo-1", ct)).HasWeight(), "a weight arrives", ct);

        var device = (await GetDeviceAsync(client, "toledo-1", ct))!.Value;
        device.GetProperty("supportedCommands").GetArrayLength().ShouldBe(0);

        var (_, json) = await PostZeroAsync(client, "toledo-1", ct);

        json.GetProperty("result").GetString().ShouldBe("NotSupported");
        json.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Zero_while_the_agent_is_disconnected_is_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server, new SimulatedDevice("scale-1", 572m, "g"));
        await agent.StartAsync(ct);
        await WaitUntilAsync(async () => (await GetDeviceAsync(client, "scale-1", ct)).HasWeight(), "a weight arrives", ct);
        var simulator = (MtSicsScaleSimulator)GetSimulatedDevice(agent);

        await agent.StopAsync(ct);
        await WaitUntilAsync(async () => (await GetDeviceAsync(client, "scale-1", ct)).State() == "Disconnected", "the device is disconnected", ct);

        var (_, json) = await PostZeroAsync(client, "scale-1", ct);

        json.GetProperty("result").GetString().ShouldBe("Unavailable");
        simulator.ZeroCommandCount.ShouldBe(0);
    }

    [Fact]
    public async Task Zero_for_an_unknown_device_is_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (_, json) = await PostZeroAsync(client, "nobody", ct);

        json.GetProperty("result").GetString().ShouldBe("Unavailable");
    }

    [Fact]
    public async Task Zero_refused_by_the_scale_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server, new SimulatedDevice("scale-1", 572m, "g", Devices.WeightState.Motion));
        await agent.StartAsync(ct);
        await WaitUntilAsync(async () => (await GetDeviceAsync(client, "scale-1", ct)).HasWeight(), "a weight arrives", ct);

        var (_, json) = await PostZeroAsync(client, "scale-1", ct);

        json.GetProperty("result").GetString().ShouldBe("Rejected");
        json.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("Z I");

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Agent_that_does_not_answer_in_time_is_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(commandTimeout: TimeSpan.FromMilliseconds(500));
        using var client = factory.CreateClient();
        var calls = 0;
        var agentTimeout = TimeSpan.Zero;
        await using var connection = CreateFakeAgent(factory.Server);
        connection.On<string, TimeSpan, AgentCommandResult>(AgentHubContract.Zero, async (_, timeout) =>
        {
            Interlocked.Increment(ref calls);
            agentTimeout = timeout;
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
            return new AgentCommandResult(DeviceCommandOutcome.Done);
        });
        await connection.StartAsync(ct);
        await RegisterAsync(connection, ct);

        var started = DateTime.UtcNow;
        var (_, json) = await PostZeroAsync(client, "scale-1", ct);

        json.GetProperty("result").GetString().ShouldBe("Unavailable");
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(2.5));
        calls.ShouldBe(1);
        agentTimeout.ShouldBeGreaterThan(TimeSpan.Zero);
        agentTimeout.ShouldBeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task Agent_answer_is_passed_through_with_its_detail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await using var connection = CreateFakeAgent(factory.Server);
        connection.On<string, TimeSpan, AgentCommandResult>(
            AgentHubContract.Zero,
            (deviceId, _) => Task.FromResult(new AgentCommandResult(DeviceCommandOutcome.Rejected, $"out of range: {deviceId}")));
        await connection.StartAsync(ct);
        await RegisterAsync(connection, ct);

        var commands = factory.Services.GetRequiredService<IDeviceCommands>();
        var result = await commands.ZeroAsync("scale-1", ct);
        var (body, _) = await PostZeroAsync(client, "scale-1", ct);

        result.ShouldBe(new DeviceCommandResult(DeviceCommandOutcome.Rejected, "out of range: scale-1"));
        body.ShouldBe("{\"result\":\"Rejected\",\"detail\":\"out of range: scale-1\"}");
    }

    private static ISimulatedDevice GetSimulatedDevice(IHost agent) =>
        agent.Services.GetRequiredService<DeviceFleet>().Devices.Single().Simulator.ShouldNotBeNull().Device;

    private static HubConnection CreateFakeAgent(TestServer server) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, AgentHubContract.Path.TrimStart('/')), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

    private static Task RegisterAsync(HubConnection connection, CancellationToken ct) =>
        connection.InvokeAsync(
            AgentHubContract.Register,
            new AgentRegistration("PC-1", "1.0.0", ["scale-1"], new Dictionary<string, IReadOnlyList<string>> { ["scale-1"] = [DeviceCommandNames.Zero] }),
            ct);
}
