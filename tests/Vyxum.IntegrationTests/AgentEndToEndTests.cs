using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Vyxum.Agent;
using Vyxum.Devices;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;
using static Vyxum.IntegrationTests.AgentTestSupport;

namespace Vyxum.IntegrationTests;

/// <summary>The web application, a real in-process agent and simulated scales, end to end.</summary>
public sealed class AgentEndToEndTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-e2e-" + Guid.NewGuid().ToString("N"));

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

    private WebApplicationFactory<Program> CreateFactory(string? key = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory);
            if (key is not null)
            {
                builder.UseSetting("Vyxum:Agents:Key", key);
            }
        });

    /// <summary>An agent connected to the in-memory test server over long polling.</summary>
    private static IHost BuildAgent(TestServer server, params SimulatedDevice[] devices) =>
        AgentTestSupport.BuildAgent(
            AgentConfig(server.BaseAddress.ToString(), key: null, devices),
            options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            });

    [Fact]
    public async Task Simulated_readings_appear_in_api_and_live_weight_source()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(
            factory.Server,
            new SimulatedDevice("scale-g", 572m, "g"),
            new SimulatedDevice("scale-kg", 1.250m, "kg", WeightState.Motion));
        await agent.StartAsync(ct);

        await WaitUntilAsync(
            async () => (await GetDeviceAsync(client, "scale-g", ct)).HasWeight() && (await GetDeviceAsync(client, "scale-kg", ct)).HasWeight(),
            "both devices have a weight",
            ct);

        var grams = (await GetDeviceAsync(client, "scale-g", ct))!.Value;
        grams.GetProperty("agentName").GetString().ShouldBe(AgentName);
        grams.GetProperty("state").GetString().ShouldBe("Connected");
        grams.GetProperty("lastWeight").GetProperty("value").GetDecimal().ShouldBe(572m);
        grams.GetProperty("lastWeight").GetProperty("unit").GetString().ShouldBe("g");
        grams.GetProperty("lastWeight").GetProperty("state").GetString().ShouldBe("Stable");

        // The scale of the value survives the whole path: device, agent, hub, store, API.
        var body = await client.GetStringAsync(DeviceEndpointExtensions.DevicesPath, ct);
        body.ShouldContain("\"value\":1.250,");
        var kilograms = (await GetDeviceAsync(client, "scale-kg", ct))!.Value.GetProperty("lastWeight");
        kilograms.GetProperty("unit").GetString().ShouldBe("kg");
        kilograms.GetProperty("state").GetString().ShouldBe("Motion");

        var source = factory.Services.GetRequiredService<ILiveWeightSource>();
        var latest = source.GetLatest("scale-kg");
        latest.ShouldNotBeNull();
        latest.Value.ShouldBe(1.250m);
        latest.Value!.Value.Scale.ShouldBe((byte)3);
        latest.Unit.ShouldBe("kg");
        latest.State.ShouldBe(LiveWeightState.Motion);
        source.GetLatest("scale-g")!.State.ShouldBe(LiveWeightState.Stable);

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Unchanged_reading_is_sent_again_every_heartbeat()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var agent = BuildAgent(factory.Server, new SimulatedDevice("scale-1", 572m, "g"));
        await agent.StartAsync(ct);
        var source = factory.Services.GetRequiredService<ILiveWeightSource>();
        await WaitUntilAsync(() => Task.FromResult(source.GetLatest("scale-1") is not null), "a weight arrives", ct);

        var first = source.GetLatest("scale-1")!.ReceivedAt;

        await WaitUntilAsync(() => Task.FromResult(source.GetLatest("scale-1")!.ReceivedAt > first), "the same weight arrives again", ct);
        source.GetLatest("scale-1")!.Value.ShouldBe(572m);

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Device_link_drop_is_reported_and_readings_resume()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server, new SimulatedDevice("scale-1", 572m, "g"));
        await agent.StartAsync(ct);
        await WaitUntilAsync(async () => (await GetDeviceAsync(client, "scale-1", ct)).HasWeight(), "a weight arrives", ct);

        var simulator = agent.Services.GetRequiredService<DeviceFleet>().Devices.Single().Simulator.ShouldNotBeNull();
        var opensBefore = simulator.OpenCount;
        var droppedAt = DateTimeOffset.UtcNow;
        simulator.FailNextOpens(3);
        simulator.Disconnect();

        string? error = null;
        await WaitUntilAsync(
            async () =>
            {
                var device = await GetDeviceAsync(client, "scale-1", ct);
                error = device.LastError();
                return device.State() == "Disconnected" && error is not null;
            },
            "the device is shown disconnected with an error",
            ct);
        error.ShouldNotBeNullOrWhiteSpace();

        var source = factory.Services.GetRequiredService<ILiveWeightSource>();
        await WaitUntilAsync(
            async () => (await GetDeviceAsync(client, "scale-1", ct)).State() == "Connected" && source.GetLatest("scale-1")!.ReceivedAt > droppedAt,
            "readings resume",
            ct);
        (await GetDeviceAsync(client, "scale-1", ct)).LastError().ShouldBeNull();
        simulator.OpenCount.ShouldBeGreaterThan(opensBefore);

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Agent_reconnects_and_registers_again_after_the_web_application_restarts()
    {
        var ct = TestContext.Current.CancellationToken;
        const string key = "test-agent-key";

        // A real Kestrel server on a loopback port, so the agent uses real WebSockets and sends the key header.
        var first = CreateKestrelFactory(key, port: 0);
        first.StartServer();
        var address = new Uri(first.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        using var client = new HttpClient { BaseAddress = address };

        using var agent = AgentTestSupport.BuildAgent(AgentConfig(address.ToString(), key, new SimulatedDevice("scale-1", 572m, "g")));
        await agent.StartAsync(ct);
        await WaitUntilAsync(async () => (await GetDeviceAsync(client, "scale-1", ct)).HasWeight(), "the first server has a weight", ct);

        await first.DisposeAsync();

        // The same address again, with an empty live weight store: the device appears only if the agent registers again.
        var second = await StartKestrelOnPortAsync(key, address.Port, ct);
        await using (second)
        {
            await WaitUntilAsync(
                async () =>
                {
                    var device = await GetDeviceAsync(client, "scale-1", ct);
                    return device.State() == "Connected" && device.HasWeight();
                },
                "the agent reconnects to the second server",
                ct);
            var device = (await GetDeviceAsync(client, "scale-1", ct))!.Value;
            device.GetProperty("agentName").GetString().ShouldBe(AgentName);
            device.GetProperty("lastWeight").GetProperty("value").GetDecimal().ShouldBe(572m);

            await agent.StopAsync(ct);
        }
    }

    [Fact]
    public async Task Agent_with_wrong_key_keeps_retrying_and_never_registers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(key: "right-key");
        using var client = factory.CreateClient();
        using var agent = AgentTestSupport.BuildAgent(
            AgentConfig(factory.Server.BaseAddress.ToString(), "wrong-key", new SimulatedDevice("scale-1", 572m, "g")),
            options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            });

        await agent.StartAsync(ct);
        await Task.Delay(TimeSpan.FromSeconds(1), ct);

        (await client.GetStringAsync(DeviceEndpointExtensions.DevicesPath, ct)).ShouldBe("[]");
        await agent.StopAsync(ct);
    }

    private WebApplicationFactory<Program> CreateKestrelFactory(string key, int port)
    {
        var factory = CreateFactory(key).WithWebHostBuilder(builder =>
            // The open agent WebSocket would otherwise hold a graceful shutdown for the default 30 seconds.
            builder.ConfigureServices(services => services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(1))));
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        return factory;
    }

    /// <summary>Starts a Kestrel server on a port that was just released, retrying while the system still holds it.</summary>
    private async Task<WebApplicationFactory<Program>> StartKestrelOnPortAsync(string key, int port, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var factory = CreateKestrelFactory(key, port);
            try
            {
                factory.StartServer();
                return factory;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await factory.DisposeAsync();
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}
