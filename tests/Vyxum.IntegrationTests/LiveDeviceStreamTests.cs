using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.IntegrationTests;

public sealed class LiveDeviceStreamTests : IDisposable
{
    private const string DeviceId = "scale-1";

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-live-" + Guid.NewGuid().ToString("N"));

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

    private static AgentReading Reading(decimal value, LiveWeightState state = LiveWeightState.Stable) =>
        new(value, "kg", state, null, null, "raw", DateTimeOffset.UtcNow);

    private static async Task<(HttpResponseMessage Response, StreamReader Reader)> OpenAsync(HttpClient client, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/devices/{DeviceId}/live");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        return (response, new StreamReader(await response.Content.ReadAsStreamAsync(ct)));
    }

    /// <summary>Reads the next Server-Sent Event and returns its data as JSON.</summary>
    private static async Task<JsonElement> NextEventAsync(StreamReader reader, CancellationToken ct)
    {
        string? data = null;
        while (true)
        {
            var line = await reader.ReadLineAsync(ct) ?? throw new ShouldAssertException("The event stream ended.");
            if (line.Length == 0 && data is not null)
            {
                using var json = JsonDocument.Parse(data);
                return json.RootElement.Clone();
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line["data: ".Length..];
            }
        }
    }

    [Fact]
    public async Task Stream_opened_before_the_agent_waits_then_delivers_state_and_weights()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AgentTestSupport.Timeout);
        var ct = timeout.Token;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<LiveWeightStore>();

        var (response, reader) = await OpenAsync(client, ct);
        using (response)
        using (reader)
        {
            var waiting = await NextEventAsync(reader, ct);
            waiting.GetProperty("deviceId").GetString().ShouldBe(DeviceId);
            waiting.GetProperty("state").GetString().ShouldBe("Disconnected");
            waiting.GetProperty("agentName").ValueKind.ShouldBe(JsonValueKind.Null);
            waiting.GetProperty("lastWeight").ValueKind.ShouldBe(JsonValueKind.Null);

            store.RegisterAgent("connection-1", "PC-1", [DeviceId]);
            var connecting = await NextEventAsync(reader, ct);
            connecting.GetProperty("state").GetString().ShouldBe("Connecting");
            connecting.GetProperty("agentName").GetString().ShouldBe("PC-1");

            store.ReportReading("connection-1", DeviceId, Reading(1.250m));
            var first = await NextEventAsync(reader, ct);
            first.GetProperty("state").GetString().ShouldBe("Connected");
            first.GetProperty("lastWeight").GetProperty("value").GetDecimal().ShouldBe(1.250m);
            first.GetProperty("lastWeight").GetProperty("state").GetString().ShouldBe("Stable");

            store.ReportReading("connection-1", DeviceId, Reading(1.250m));
            store.ReportReading("connection-1", DeviceId, Reading(2.500m, LiveWeightState.Motion));
            var second = await NextEventAsync(reader, ct);
            second.GetProperty("lastWeight").GetProperty("value").GetDecimal().ShouldBe(2.500m);
            second.GetProperty("lastWeight").GetProperty("state").GetString().ShouldBe("Motion");
        }
    }

    [Fact]
    public async Task Stream_of_a_known_device_starts_with_its_latest_weight()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AgentTestSupport.Timeout);
        var ct = timeout.Token;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<LiveWeightStore>();
        store.RegisterAgent("connection-1", "PC-1", [DeviceId]);
        store.ReportReading("connection-1", DeviceId, Reading(0.750m));

        var (response, reader) = await OpenAsync(client, ct);
        using (response)
        using (reader)
        {
            var first = await NextEventAsync(reader, ct);
            first.GetProperty("state").GetString().ShouldBe("Connected");
            first.GetProperty("lastWeight").GetProperty("value").GetDecimal().ShouldBe(0.750m);

            store.DisconnectAgent("connection-1");
            var disconnected = await NextEventAsync(reader, ct);
            disconnected.GetProperty("state").GetString().ShouldBe("Disconnected");
            disconnected.GetProperty("lastWeight").GetProperty("value").GetDecimal().ShouldBe(0.750m);
        }
    }
}
