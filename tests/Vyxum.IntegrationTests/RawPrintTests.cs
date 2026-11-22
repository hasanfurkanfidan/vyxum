using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;
using static Vyxum.IntegrationTests.AgentTestSupport;

namespace Vyxum.IntegrationTests;

/// <summary>Print jobs end to end: platform channel, hub client result, agent, and a network printer stand-in.</summary>
public sealed class RawPrintTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-print-" + Guid.NewGuid().ToString("N"));

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

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory));

    private static IHost BuildAgent(TestServer server, int? allowedPort, bool enabled = true)
    {
        var settings = AgentConfig(server.BaseAddress.ToString(), key: null);
        settings["Vyxum:Agent:Printing:Enabled"] = enabled ? "true" : "false";
        settings["Vyxum:Agent:Printing:ConnectTimeout"] = "00:00:02";
        if (allowedPort is { } port)
        {
            settings["Vyxum:Agent:Printing:AllowedTcpPorts:0"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return AgentTestSupport.BuildAgent(settings, options =>
        {
            options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
        });
    }

    private static RawPrinterTarget Network(int port, string? agentName = null) =>
        new(agentName, RawPrinterConnection.Network, "127.0.0.1", port);

    private static async Task WaitForPrintingAgentAsync(IRawPrinterChannel channel, CancellationToken ct) =>
        await WaitUntilAsync(() => Task.FromResult(channel.GetPrintingAgents().Count > 0), "the agent can print", ct);

    /// <summary>Accepts one connection, reads until the expected byte count, then answers.</summary>
    private static async Task<byte[]> ServeOnceAsync(TcpListener listener, int expected, byte[]? answer, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        var stream = client.GetStream();
        var received = new MemoryStream();
        var buffer = new byte[4096];
        while (received.Length < expected)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                break;
            }

            received.Write(buffer, 0, read);
        }

        if (answer is not null)
        {
            await stream.WriteAsync(answer, ct);
            await stream.FlushAsync(ct);
        }

        // Keep the connection open briefly so that the agent reads the answer before the close.
        await Task.Delay(200, ct);
        return received.ToArray();
    }

    [Fact]
    public async Task Job_reaches_the_printer_once_and_the_answer_comes_back()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var factory = CreateFactory();
        using var agent = BuildAgent(factory.Server, port);
        await agent.StartAsync(ct);
        var channel = factory.Services.GetRequiredService<IRawPrinterChannel>();
        await WaitForPrintingAgentAsync(channel, ct);
        channel.GetPrintingAgents().ShouldBe([AgentName]);

        var job = Encoding.ASCII.GetBytes("^XA^FDTest^FS^XZ~HS");
        var printer = ServeOnceAsync(listener, job.Length, Encoding.ASCII.GetBytes("\u0002030,0,0\u0003\r\nrest"), ct);

        var result = await channel.SendAsync(
            new RawPrintRequest(Network(port), job, ReplyMaxBytes: 64, ReplyTimeout: TimeSpan.FromSeconds(2), ReplyEndByte: 0x03),
            ct);

        result.Outcome.ShouldBe(RawPrintOutcome.Sent, result.Detail);
        (await printer).ShouldBe(job);
        Encoding.ASCII.GetString(result.Reply.ShouldNotBeNull()).ShouldBe("\u0002030,0,0\u0003");

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Answer_of_several_blocks_is_read_up_to_the_last_end_byte()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var factory = CreateFactory();
        using var agent = BuildAgent(factory.Server, port);
        await agent.StartAsync(ct);
        var channel = factory.Services.GetRequiredService<IRawPrinterChannel>();
        await WaitForPrintingAgentAsync(channel, ct);

        const string answer = "\u0002a\u0003\r\n\u0002b\u0003\r\n\u0002c\u0003";
        var printer = ServeOnceAsync(listener, 3, Encoding.ASCII.GetBytes(answer + "\r\nmore"), ct);

        var started = DateTime.UtcNow;
        var result = await channel.SendAsync(
            new RawPrintRequest(Network(port), "~HS"u8.ToArray(), ReplyMaxBytes: 256, ReplyTimeout: TimeSpan.FromSeconds(5), ReplyEndByte: 0x03, ReplyEndCount: 3),
            ct);

        result.Outcome.ShouldBe(RawPrintOutcome.Sent, result.Detail);
        Encoding.ASCII.GetString(result.Reply.ShouldNotBeNull()).ShouldBe(answer);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(4));
        await printer;

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Job_without_reply_needs_no_answer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var factory = CreateFactory();
        using var agent = BuildAgent(factory.Server, port);
        await agent.StartAsync(ct);
        var channel = factory.Services.GetRequiredService<IRawPrinterChannel>();
        await WaitForPrintingAgentAsync(channel, ct);

        var job = new byte[100_000];
        Random.Shared.NextBytes(job);
        var printer = ServeOnceAsync(listener, job.Length, answer: null, ct);

        var result = await channel.SendAsync(new RawPrintRequest(Network(port, AgentName.ToUpperInvariant()), job), ct);

        result.Outcome.ShouldBe(RawPrintOutcome.Sent, result.Detail);
        result.Reply.ShouldBe([]);
        (await printer).ShouldBe(job);

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Port_that_is_not_allowed_is_refused_without_connecting()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var factory = CreateFactory();

        // Only the default port 9100 is allowed.
        using var agent = BuildAgent(factory.Server, allowedPort: null);
        await agent.StartAsync(ct);
        var channel = factory.Services.GetRequiredService<IRawPrinterChannel>();
        await WaitForPrintingAgentAsync(channel, ct);

        var result = await channel.SendAsync(new RawPrintRequest(Network(port), "data"u8.ToArray()), ct);

        result.Outcome.ShouldBe(RawPrintOutcome.Refused);
        result.Detail.ShouldNotBeNull().ShouldContain("9100");
        listener.Pending().ShouldBeFalse();

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Unreachable_printer_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        await using var factory = CreateFactory();
        using var agent = BuildAgent(factory.Server, port);
        await agent.StartAsync(ct);
        var channel = factory.Services.GetRequiredService<IRawPrinterChannel>();
        await WaitForPrintingAgentAsync(channel, ct);

        var result = await channel.SendAsync(new RawPrintRequest(Network(port), "data"u8.ToArray()), ct);

        result.Outcome.ShouldBe(RawPrintOutcome.Failed);

        await agent.StopAsync(ct);
    }

    [Fact]
    public async Task Without_a_printing_agent_nothing_is_sent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var agent = BuildAgent(factory.Server, 9100, enabled: false);
        await agent.StartAsync(ct);
        var store = factory.Services.GetRequiredService<LiveWeightStore>();
        await WaitUntilAsync(() => Task.FromResult(store.GetAgents().Count > 0), "the agent registers", ct);
        var channel = factory.Services.GetRequiredService<IRawPrinterChannel>();

        channel.GetPrintingAgents().ShouldBeEmpty();
        (await channel.SendAsync(new RawPrintRequest(Network(9100), "data"u8.ToArray()), ct)).Outcome.ShouldBe(RawPrintOutcome.Unavailable);
        (await channel.SendAsync(new RawPrintRequest(Network(9100, "other-pc"), "data"u8.ToArray()), ct)).Outcome.ShouldBe(RawPrintOutcome.Unavailable);

        await agent.StopAsync(ct);
    }

    [Theory]
    [InlineData(RawPrinterConnection.Network, null, 9100, null, null)]
    [InlineData(RawPrinterConnection.Network, "10.0.0.5", 0, null, null)]
    [InlineData(RawPrinterConnection.Serial, null, null, null, 9600)]
    [InlineData(RawPrinterConnection.Serial, null, null, "COM4", 0)]
    public async Task Invalid_target_is_unavailable(RawPrinterConnection connection, string? host, int? port, string? portName, int? baudRate)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        var channel = factory.Services.GetRequiredService<IRawPrinterChannel>();

        var result = await channel.SendAsync(
            new RawPrintRequest(new RawPrinterTarget(null, connection, host, port, portName, baudRate), "data"u8.ToArray()), ct);

        result.Outcome.ShouldBe(RawPrintOutcome.Unavailable);
        result.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Empty_or_oversized_job_is_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        var channel = factory.Services.GetRequiredService<IRawPrinterChannel>();

        (await channel.SendAsync(new RawPrintRequest(Network(9100), Array.Empty<byte>()), ct)).Outcome.ShouldBe(RawPrintOutcome.Unavailable);
        (await channel.SendAsync(new RawPrintRequest(Network(9100), new byte[RawPrinterLimits.MaxDataBytes + 1]), ct)).Outcome.ShouldBe(RawPrintOutcome.Unavailable);
        (await channel.SendAsync(new RawPrintRequest(Network(9100), "x"u8.ToArray(), ReplyMaxBytes: RawPrinterLimits.MaxReplyBytes + 1), ct)).Outcome.ShouldBe(RawPrintOutcome.Unavailable);
    }
}
