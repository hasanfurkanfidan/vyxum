using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.IntegrationTests;

public sealed class AgentHubTests : IDisposable
{
    private const string DeviceId = "scale-1";
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("192.168.1.50");

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-agent-" + Guid.NewGuid().ToString("N"));

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

    private WebApplicationFactory<Program> CreateFactory(string? key = null, IPAddress? remoteAddress = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory);
            if (key is not null)
            {
                builder.UseSetting("Vyxum:Agents:Key", key);
            }

            if (remoteAddress is not null)
            {
                builder.ConfigureServices(services =>
                    services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(remoteAddress)));
            }
        });

    private static HubConnection CreateConnection(
        TestServer server,
        HttpTransportType transport = HttpTransportType.LongPolling,
        string? headerKey = null,
        string? queryKey = null)
    {
        var path = AgentHubContract.Path.TrimStart('/');
        if (queryKey is not null)
        {
            path += $"?access_token={Uri.EscapeDataString(queryKey)}";
        }

        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, path), options =>
            {
                options.Transports = transport;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                if (headerKey is not null)
                {
                    options.Headers[AgentHubContract.KeyHeader] = headerKey;
                }

                if (transport == HttpTransportType.WebSockets)
                {
                    options.SkipNegotiation = true;
                    options.WebSocketFactory = async (context, ct) =>
                    {
                        var client = server.CreateWebSocketClient();
                        if (headerKey is not null)
                        {
                            client.ConfigureRequest = request => request.Headers[AgentHubContract.KeyHeader] = headerKey;
                        }

                        return await client.ConnectAsync(context.Uri, ct);
                    };
                }
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
    }

    private static async Task RegisterAndReportAsync(HubConnection connection, CancellationToken ct)
    {
        await connection.InvokeAsync(
            AgentHubContract.Register, new AgentRegistration("PC-1", "1.0.0", [DeviceId]), ct);
        await connection.InvokeAsync(
            AgentHubContract.ReportStatus,
            DeviceId,
            new AgentDeviceStatus(DeviceConnectionState.Connecting, null, null, DateTimeOffset.UtcNow),
            ct);
        await connection.InvokeAsync(
            AgentHubContract.ReportReading,
            DeviceId,
            new AgentReading(1.250m, "kg", LiveWeightState.Stable, 0.100m, true, "S S      1.250 kg", DateTimeOffset.UtcNow),
            ct);
    }

    private static async Task<JsonElement> GetDeviceAsync(HttpClient client, CancellationToken ct)
    {
        var body = await client.GetStringAsync(DeviceEndpointExtensions.DevicesPath, ct);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.EnumerateArray().Single(d => d.GetProperty("deviceId").GetString() == DeviceId).Clone();
    }

    [Theory]
    [InlineData(HttpTransportType.LongPolling)]
    [InlineData(HttpTransportType.WebSockets)]
    public async Task Reported_reading_appears_in_api_and_live_weight_source(HttpTransportType transport)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await using var connection = CreateConnection(factory.Server, transport);
        await connection.StartAsync(ct);

        await RegisterAndReportAsync(connection, ct);

        var body = await client.GetStringAsync(DeviceEndpointExtensions.DevicesPath, ct);
        body.ShouldContain("\"value\":1.250");
        var device = await GetDeviceAsync(client, ct);
        device.GetProperty("agentName").GetString().ShouldBe("PC-1");
        device.GetProperty("state").GetString().ShouldBe("Connected");
        var weight = device.GetProperty("lastWeight");
        weight.GetProperty("value").GetDecimal().ShouldBe(1.250m);
        weight.GetProperty("unit").GetString().ShouldBe("kg");
        weight.GetProperty("state").GetString().ShouldBe("Stable");
        weight.GetProperty("tare").GetDecimal().ShouldBe(0.100m);
        weight.GetProperty("isNet").GetBoolean().ShouldBeTrue();

        var latest = factory.Services.GetRequiredService<ILiveWeightSource>().GetLatest(DeviceId);
        latest.ShouldNotBeNull();
        latest.Value.ShouldBe(1.250m);
        latest.State.ShouldBe(LiveWeightState.Stable);
    }

    [Fact]
    public async Task Device_shows_disconnected_after_agent_disconnects()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await using var connection = CreateConnection(factory.Server);
        await connection.StartAsync(ct);
        await RegisterAndReportAsync(connection, ct);

        await connection.StopAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string? state;
        do
        {
            await Task.Delay(50, timeout.Token);
            state = (await GetDeviceAsync(client, timeout.Token)).GetProperty("state").GetString();
        }
        while (state != "Disconnected");

        // The last weight stays available after the disconnect.
        factory.Services.GetRequiredService<ILiveWeightSource>().GetLatest(DeviceId).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Configured_key_rejects_missing_or_wrong_key(string? suppliedKey)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(key: "test-agent-key");
        await using var connection = CreateConnection(factory.Server, headerKey: suppliedKey);

        var exception = await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync(ct));

        exception.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(HttpTransportType.LongPolling)]
    [InlineData(HttpTransportType.WebSockets)]
    public async Task Configured_key_accepts_correct_key_from_remote_address(HttpTransportType transport)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(key: "test-agent-key", remoteAddress: RemoteAddress);
        using var client = factory.CreateClient();
        await using var connection = CreateConnection(factory.Server, transport, headerKey: "test-agent-key");

        await connection.StartAsync(ct);
        await RegisterAndReportAsync(connection, ct);

        (await GetDeviceAsync(client, ct)).GetProperty("state").GetString().ShouldBe("Connected");
    }

    [Fact]
    public async Task Configured_key_in_query_string_is_rejected()
    {
        // Query strings end up in request logs, so the key is accepted only in the header.
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(key: "test-agent-key");
        await using var connection = CreateConnection(factory.Server, queryKey: "test-agent-key");

        var exception = await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync(ct));

        exception.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Without_key_a_remote_address_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(remoteAddress: RemoteAddress);
        await using var connection = CreateConnection(factory.Server);

        var exception = await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync(ct));

        exception.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_key_a_loopback_address_is_accepted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(remoteAddress: IPAddress.Loopback);
        await using var connection = CreateConnection(factory.Server);

        await connection.StartAsync(ct);

        connection.State.ShouldBe(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Devices_endpoint_is_empty_without_agents()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync(DeviceEndpointExtensions.DevicesPath, ct);

        body.ShouldBe("[]");
    }

    /// <summary>Makes every request look as if it came from <paramref name="address"/>.</summary>
    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
