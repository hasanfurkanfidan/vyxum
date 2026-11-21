using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Vyxum.Agent;
using Vyxum.Agent.Configuration;
using Vyxum.Agent.Discovery;
using Vyxum.Devices;
using Vyxum.Devices.Discovery;
using Vyxum.Devices.Simulator;
using Vyxum.Devices.Transports;
using Vyxum.Platform;
using Xunit;
using static Vyxum.IntegrationTests.AgentTestSupport;
using AgentDeviceConfig = Vyxum.Agent.Contract.AgentDeviceConfig;
using DiscoveryPortState = Vyxum.Agent.Contract.DiscoveryPortState;
using DiscoveryPortStatus = Vyxum.Agent.Contract.DiscoveryPortStatus;
using DiscoveryRequest = Vyxum.Agent.Contract.DiscoveryRequest;

namespace Vyxum.IntegrationTests;

/// <summary>
/// Device discovery end to end (decision V-40): the web application starts a job, a real in-process agent scans virtual
/// serial ports (simulated devices behind a fake port list) and the API returns what it found.
/// </summary>
public sealed class DiscoveryApiTests : IDisposable
{
    private static readonly string[] AllowedWrites = ["SI\r\n", "I4\r\n"];

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-disc-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> _writes = new();

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

    private WebApplicationFactory<Program> CreateFactory(TimeSpan? discoveryTimeout = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory);
            if (discoveryTimeout is { } timeout)
            {
                builder.UseSetting("Vyxum:Agents:DiscoveryTimeout", timeout.ToString("c"));
            }
        });

    /// <summary>Virtual ports: COM3 has an IND231-like MT-SICS scale at 9600 8N1 (ES on the first command); the others are silent.</summary>
    private void ConfigurePorts(PortScannerOptions options, TimeSpan listen, params string[] silentPorts)
    {
        var scale = new MtSicsScaleSimulator(new FixedWeightSource(572m, "g"), "C139673552") { FirstCommandReturnsSyntaxError = true };
        options.ListenTime = listen;
        options.ResponseTimeout = TimeSpan.FromMilliseconds(150);
        options.GetPortNames = () => ["COM3", .. silentPorts];
        options.CreateSerialTransport = serial =>
        {
            ITransport inner = serial is { PortName: "COM3", BaudRate: 9600, DataBits: 8, Parity: SerialParity.None }
                ? new SimulatedTransport(scale, "COM3 sim")
                : new SilentTransport();
            return new Recording(inner, bytes => _writes.Enqueue(Encoding.Latin1.GetString(bytes)));
        };
    }

    private IHost BuildAgent(TestServer server, Action<PortScannerOptions> configurePorts) =>
        AgentTestSupport.BuildAgent(
            AgentConfig(server.BaseAddress.ToString(), key: null),
            options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            },
            services => services.Configure(configurePorts));

    private static async Task<JsonElement> StartAsync(HttpClient client, string? body, HttpStatusCode expected)
    {
        using var content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(DeviceEndpointExtensions.DiscoverPath, content, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private static async Task<JsonElement> GetJobAsync(HttpClient client, string id)
    {
        var text = await client.GetStringAsync(DeviceEndpointExtensions.DiscoverPath + "/" + id, Ct);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private static async Task<JsonElement> WaitForEndAsync(HttpClient client, string id)
    {
        JsonElement job = default;
        await WaitUntilAsync(
            async () =>
            {
                job = await GetJobAsync(client, id);
                return job.GetProperty("status").GetString() != "Running";
            },
            "the discovery ends",
            Ct);
        return job;
    }

    private static Task WaitForAgentAsync(WebApplicationFactory<Program> factory) =>
        WaitUntilAsync(
            () => Task.FromResult(factory.Services.GetRequiredService<LiveWeightStore>().GetAgents().Count > 0),
            "the agent registers",
            Ct);

    private void OnlyReadCommandsWereWritten()
    {
        _writes.ShouldNotBeEmpty();
        _writes.ShouldAllBe(w => AllowedWrites.Contains(w));
    }

    [Fact]
    public async Task Without_an_agent_discovery_is_503()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var answer = await StartAsync(client, null, HttpStatusCode.ServiceUnavailable);

        answer.GetProperty("code").GetString().ShouldBe("devices.NoAgent");
    }

    [Fact]
    public async Task Unknown_job_is_404_and_bad_address_is_400()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(DeviceEndpointExtensions.DiscoverPath + "/nope", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problem = await StartAsync(client, """{"tcp":{"host":" ","port":70000}}""", HttpStatusCode.BadRequest);
        problem.GetProperty("errors").GetProperty("tcp.host")[0].GetString().ShouldBe("devices.HostRequired");
        problem.GetProperty("errors").GetProperty("tcp.port")[0].GetString().ShouldBe("devices.PortInvalid");
    }

    [Fact]
    public async Task Scale_is_found_and_its_settings_can_be_saved_as_they_are()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server, o => ConfigurePorts(o, TimeSpan.FromMilliseconds(100), "COM1"));
        await agent.StartAsync(Ct);
        await WaitForAgentAsync(factory);

        var started = await StartAsync(client, "{}", HttpStatusCode.Accepted);
        var id = started.GetProperty("id").GetString().ShouldNotBeNull();
        var job = await WaitForEndAsync(client, id);

        job.GetProperty("status").GetString().ShouldBe("Completed", job.ToString());
        job.GetProperty("code").ValueKind.ShouldBe(JsonValueKind.Null);
        job.GetProperty("id").GetString().ShouldBe(id);
        job.GetProperty("agentName").GetString().ShouldBe(AgentName);
        job.GetProperty("startedAt").GetDateTimeOffset().ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
        var ports = job.GetProperty("ports").EnumerateArray()
            .Select(p => (p.GetProperty("portName").GetString(), p.GetProperty("state").GetString(), p.GetProperty("detail").ValueKind))
            .ToList();
        ports.ShouldBe([("COM3", "Found", JsonValueKind.Null), ("COM1", "NotFound", JsonValueKind.Null)]);

        var candidate = job.GetProperty("candidates").EnumerateArray().ShouldHaveSingleItem();
        candidate.GetProperty("key").GetString().ShouldBe("serial:COM3");
        candidate.GetProperty("portName").GetString().ShouldBe("COM3");
        candidate.GetProperty("host").ValueKind.ShouldBe(JsonValueKind.Null);
        candidate.GetProperty("port").ValueKind.ShouldBe(JsonValueKind.Null);
        candidate.GetProperty("serialNumber").GetString().ShouldBe("C139673552");
        candidate.GetProperty("description").GetString().ShouldBe("MT-SICS, 9600 8N1");
        var reading = candidate.GetProperty("reading");
        reading.GetProperty("value").GetString().ShouldBe("572");
        reading.GetProperty("unit").GetString().ShouldBe("g");
        reading.GetProperty("state").GetString().ShouldBe("Stable");
        candidate.GetProperty("transport").ToString().ShouldBe(
            """{"type":"serial","portName":"COM3","baudRate":9600,"dataBits":8,"parity":"None","stopBits":"One","handshake":"None","dtrEnable":true,"rtsEnable":true}""");
        candidate.GetProperty("protocol").ToString().ShouldBe("""{"type":"mt-sics","command":"SI"}""");
        OnlyReadCommandsWereWritten();

        // The UI posts the candidate's objects straight to the device settings.
        var device = $$"""{"id":"scale-1","name":"Scale","transport":{{candidate.GetProperty("transport")}},"protocol":{{candidate.GetProperty("protocol")}}}""";
        using var save = await client.PostAsync(DeviceEndpointExtensions.ConfigPath, new StringContent(device, Encoding.UTF8, "application/json"), Ct);
        save.StatusCode.ShouldBe(HttpStatusCode.Created, await save.Content.ReadAsStringAsync(Ct));

        await agent.StopAsync(Ct);
    }

    [Fact]
    public async Task Second_discovery_on_the_same_agent_is_409_and_progress_is_visible()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Long listening on silent ports keeps the scan running for a few seconds.
        using var agent = BuildAgent(factory.Server, o => ConfigurePorts(o, TimeSpan.FromMilliseconds(400), "COM1", "COM2"));
        await agent.StartAsync(Ct);
        await WaitForAgentAsync(factory);

        var id = (await StartAsync(client, null, HttpStatusCode.Accepted)).GetProperty("id").GetString()!;
        var conflict = await StartAsync(client, $$"""{"agentName":"{{AgentName.ToUpperInvariant()}}"}""", HttpStatusCode.Conflict);
        conflict.GetProperty("code").GetString().ShouldBe("devices.DiscoveryRunning");

        await WaitUntilAsync(
            async () =>
            {
                var running = await GetJobAsync(client, id);
                return running.GetProperty("status").GetString() == "Running"
                    && running.GetProperty("ports").EnumerateArray().Any(p => p.GetProperty("state").GetString() == "Scanning");
            },
            "the progress shows a port being scanned",
            Ct);

        var job = await WaitForEndAsync(client, id);
        job.GetProperty("status").GetString().ShouldBe("Completed");
        job.GetProperty("candidates").GetArrayLength().ShouldBe(1);

        // Finished: a new discovery may start.
        var again = await StartAsync(client, null, HttpStatusCode.Accepted);
        await WaitForEndAsync(client, again.GetProperty("id").GetString()!);
        await agent.StopAsync(Ct);
    }

    [Fact]
    public async Task Unknown_agent_name_is_503()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server, o => ConfigurePorts(o, TimeSpan.FromMilliseconds(50)));
        await agent.StartAsync(Ct);
        await WaitForAgentAsync(factory);

        var answer = await StartAsync(client, """{"agentName":"other-pc"}""", HttpStatusCode.ServiceUnavailable);

        answer.GetProperty("code").GetString().ShouldBe("devices.NoAgent");
        await agent.StopAsync(Ct);
    }

    [Fact]
    public async Task Agent_that_does_not_answer_in_time_fails_the_job()
    {
        await using var factory = CreateFactory(discoveryTimeout: TimeSpan.FromMilliseconds(500));
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server, o => ConfigurePorts(o, TimeSpan.FromSeconds(1), "COM1"));
        await agent.StartAsync(Ct);
        await WaitForAgentAsync(factory);

        var id = (await StartAsync(client, null, HttpStatusCode.Accepted)).GetProperty("id").GetString()!;
        var job = await WaitForEndAsync(client, id);

        job.GetProperty("status").GetString().ShouldBe("Failed");
        job.GetProperty("code").GetString().ShouldBe("devices.AgentTimeout");
        job.GetProperty("candidates").GetArrayLength().ShouldBe(0);
        await agent.StopAsync(Ct);
    }

    [Fact]
    public async Task Network_address_is_probed_through_the_api()
    {
        var scale = new MtSicsScaleSimulator(new FixedWeightSource(1.250m, "kg", WeightState.Motion), "NET0001");
        await using var server = new SimulatorTcpServer(scale).Start();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var agent = BuildAgent(factory.Server, o =>
        {
            ConfigurePorts(o, TimeSpan.FromMilliseconds(100));
            o.GetPortNames = () => throw new InvalidOperationException("No serial port may be scanned.");
        });
        await agent.StartAsync(Ct);
        await WaitForAgentAsync(factory);

        var id = (await StartAsync(client, $$$"""{"tcp":{"host":"127.0.0.1","port":{{{server.Port}}}}}""", HttpStatusCode.Accepted))
            .GetProperty("id").GetString()!;
        var job = await WaitForEndAsync(client, id);

        job.GetProperty("status").GetString().ShouldBe("Completed", job.ToString());
        var port = job.GetProperty("ports").EnumerateArray().ShouldHaveSingleItem();
        port.GetProperty("portName").GetString().ShouldBe($"127.0.0.1:{server.Port}");
        port.GetProperty("state").GetString().ShouldBe("Found");
        var candidate = job.GetProperty("candidates").EnumerateArray().ShouldHaveSingleItem();
        candidate.GetProperty("key").GetString().ShouldBe($"tcp:127.0.0.1:{server.Port}");
        candidate.GetProperty("portName").ValueKind.ShouldBe(JsonValueKind.Null);
        candidate.GetProperty("host").GetString().ShouldBe("127.0.0.1");
        candidate.GetProperty("port").GetInt32().ShouldBe(server.Port);
        candidate.GetProperty("transport").ToString().ShouldBe($$"""{"type":"tcp","host":"127.0.0.1","port":{{server.Port}}}""");
        candidate.GetProperty("reading").GetProperty("value").GetString().ShouldBe("1.250");
        candidate.GetProperty("reading").GetProperty("state").GetString().ShouldBe("Motion");
        scale.ReceivedCommands.ShouldBe(["SI", "I4"]);
        await agent.StopAsync(Ct);
    }

    [Fact]
    public async Task Agent_skips_the_ports_of_its_running_devices()
    {
        var settings = Options.Create(new AgentSettings { AgentName = AgentName });
        var fleet = new DeviceFleet(settings, NullLogger<DeviceFleet>.Instance, TimeProvider.System);

        // The fleet is not running, so the device is never started and its port never opened.
        using var transport = JsonDocument.Parse("""{"type":"serial","portName":"COM3"}""");
        using var protocol = JsonDocument.Parse("""{"type":"mt-sics"}""");
        await fleet.ApplyServerDevicesAsync([new AgentDeviceConfig("scale-1", transport.RootElement.Clone(), protocol.RootElement.Clone())], Ct);
        fleet.GetPortsInUse().ShouldBe(["COM3"]);
        fleet.GetPortUsers().ShouldBe(new Dictionary<string, string> { ["COM3"] = "scale-1" });

        var scanner = new PortScannerOptions();
        ConfigurePorts(scanner, TimeSpan.FromMilliseconds(50), "COM1");
        var discovery = new DeviceDiscovery(fleet, Options.Create(scanner), TimeProvider.System, NullLogger<DeviceDiscovery>.Instance);
        var progress = new List<IReadOnlyList<DiscoveryPortState>>();

        var result = await discovery.DiscoverAsync(
            new DiscoveryRequest("job-1"),
            ports =>
            {
                lock (progress)
                {
                    progress.Add(ports);
                }

                return Task.CompletedTask;
            },
            Ct);

        result.Ports.ShouldBe([
            new DiscoveryPortState("COM3", DiscoveryPortStatus.InUse, "scale-1"),
            new DiscoveryPortState("COM1", DiscoveryPortStatus.NotFound, null),
        ]);
        result.Candidates.ShouldBeEmpty();
        progress.ShouldNotBeEmpty();
        progress[^1].ShouldBe(result.Ports);
    }

    [Fact]
    public async Task Agent_runs_one_discovery_at_a_time()
    {
        var fleet = new DeviceFleet(Options.Create(new AgentSettings()), NullLogger<DeviceFleet>.Instance, TimeProvider.System);
        var scanner = new PortScannerOptions();
        ConfigurePorts(scanner, TimeSpan.FromMilliseconds(300), "COM1");
        var discovery = new DeviceDiscovery(fleet, Options.Create(scanner), TimeProvider.System, NullLogger<DeviceDiscovery>.Instance);

        var first = discovery.DiscoverAsync(new DiscoveryRequest("a"), null, Ct);
        await Should.ThrowAsync<InvalidOperationException>(() => discovery.DiscoverAsync(new DiscoveryRequest("b"), null, Ct));

        (await first).Candidates.ShouldHaveSingleItem().Key.ShouldBe("serial:COM3");
    }

    /// <summary>A port where nothing ever answers.</summary>
    private sealed class SilentTransport : ITransport
    {
        private readonly Channel<byte[]> _never = Channel.CreateUnbounded<byte[]>();

        public string Description => "silent";

        public Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CloseAsync() => Task.CompletedTask;

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await _never.Reader.ReadAsync(cancellationToken);
            return 0;
        }

        public void DiscardInput()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Records every write of an inner transport.</summary>
    private sealed class Recording(ITransport inner, Action<byte[]> record) : ITransport
    {
        public string Description => inner.Description;

        public Task OpenAsync(CancellationToken cancellationToken) => inner.OpenAsync(cancellationToken);

        public Task CloseAsync() => inner.CloseAsync();

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            record(buffer.ToArray());
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) => inner.ReadAsync(buffer, cancellationToken);

        public void DiscardInput() => inner.DiscardInput();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
