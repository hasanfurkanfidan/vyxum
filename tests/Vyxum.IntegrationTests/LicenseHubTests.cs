using System.Diagnostics;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>A paid module's hub follows the license on connect, on every invocation and while connected.</summary>
public sealed class LicenseHubTests : IDisposable
{
    private const string HubPath = "/hubs/samples-paid";

    private readonly TestLicenseIssuer _issuer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _issuer.Dispose();

    private static async Task ShouldUploadAsync(LicenseTestApp app, string file)
    {
        using var response = await app.UploadAsync(file);
        var (status, body) = await LicenseTestApp.ReadAsync(response);
        status.ShouldBe(200, body.ToString());
    }

    /// <summary>A WebSocket connection opened without negotiation (a plain GET to the hub).</summary>
    private static HubConnection Connect(LicenseTestApp app, HttpTransportType transport = HttpTransportType.WebSockets) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(app.Server.BaseAddress, HubPath), options =>
            {
                options.Transports = transport;
                options.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
                if (transport == HttpTransportType.WebSockets)
                {
                    options.SkipNegotiation = true;
                    options.WebSocketFactory = async (context, ct) => await app.Server.CreateWebSocketClient().ConnectAsync(context.Uri, ct);
                }
            })
            .Build();

    /// <summary>True when the connection can be used to call Ping.</summary>
    private static async Task<bool> PingsAsync(HubConnection connection)
    {
        try
        {
            if (connection.State == HubConnectionState.Disconnected)
            {
                await connection.StartAsync(Ct);
            }

            return await connection.InvokeAsync<string>("Ping", Ct) == "pong";
        }
        catch (Exception exception) when (exception is HubException or InvalidOperationException or HttpRequestException or System.Net.WebSockets.WebSocketException or TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task WaitUntilClosedAsync(HubConnection connection)
    {
        var watch = Stopwatch.StartNew();
        while (connection.State != HubConnectionState.Disconnected)
        {
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
            await Task.Delay(20, Ct);
        }
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_read_only_module_rejects_a_websocket_connection_without_negotiation(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using var app = new LicenseTestApp(database, _issuer, clock, maintenance: false);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today, graceDays: 0)));
        clock.Advance(TimeSpan.FromDays(1));
        app.Services.GetRequiredService<ILicenseState>().Status.ShouldBe(LicenseCondition.ReadOnly);

        await using var connection = Connect(app);

        (await PingsAsync(connection)).ShouldBeFalse();
        await WaitUntilClosedAsync(connection);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_open_connection_is_closed_when_the_license_is_removed(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = new LicenseTestApp(database, _issuer, maintenance: false);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload()));
        await using var webSocket = Connect(app);
        await using var longPolling = Connect(app, HttpTransportType.LongPolling);
        (await PingsAsync(webSocket)).ShouldBeTrue();
        (await PingsAsync(longPolling)).ShouldBeTrue();

        (await app.SendAsync(HttpMethod.Delete, LicenseEndpointExtensions.LicensePath)).Status.ShouldBe(204);

        await WaitUntilClosedAsync(webSocket);
        await WaitUntilClosedAsync(longPolling);
        (await PingsAsync(webSocket)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_open_connection_is_closed_when_the_grace_period_ends(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using var app = new LicenseTestApp(database, _issuer, clock, maintenance: false);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today, graceDays: 1)));
        await using var connection = Connect(app);
        (await PingsAsync(connection)).ShouldBeTrue();

        clock.Advance(TimeSpan.FromDays(1));
        (await PingsAsync(connection)).ShouldBeTrue();

        // The next evaluation (here: the read below; in the product also the maintenance service) sees read-only.
        clock.Advance(TimeSpan.FromDays(1));
        app.Services.GetRequiredService<ILicenseState>().Status.ShouldBe(LicenseCondition.ReadOnly);
        await WaitUntilClosedAsync(connection);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_invocation_is_rejected_when_the_module_may_not_run(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using var app = new LicenseTestApp(database, _issuer, clock, maintenance: false);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today, graceDays: 0)));
        await using var connection = Connect(app);
        (await PingsAsync(connection)).ShouldBeTrue();

        // Without the abort on license changes, the check on every invocation still denies.
        var hubs = app.Services.GetRequiredService<LicensedHubs>();
        app.Licenses.Changed -= hubs.AbortDisallowed;
        clock.Advance(TimeSpan.FromDays(1));

        var error = await Should.ThrowAsync<HubException>(() => connection.InvokeAsync<string>("Ping", Ct));
        error.Message.ShouldContain("license_read_only");
    }
}
