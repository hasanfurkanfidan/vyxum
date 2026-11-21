using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vyxum.Licensing;
using Vyxum.Platform;
using Vyxum.Platform.Testing;
using Vyxum.Samples.Paid;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>
/// Signs licenses with a key generated for the test run and trusts only that key. No key material is stored anywhere.
/// </summary>
internal sealed class TestLicenseIssuer : IDisposable
{
    public const string KeyId = "test-key";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public TestLicenseIssuer()
    {
        KeyRing = new LicenseKeyRing([KeyValuePair.Create(KeyId, _key.ExportSubjectPublicKeyInfo())]);
    }

    /// <summary>A key ring that trusts only this issuer's key.</summary>
    public LicenseKeyRing KeyRing { get; }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static LicensePayload Payload(
        string licenseId = "lic-1",
        int stations = 2,
        DateOnly? validUntil = null,
        int graceDays = LicensePayload.DefaultGraceDays,
        params string[] modules) => new()
        {
            LicenseId = licenseId,
            Type = LicenseType.Subscription,
            Licensee = new Licensee { Name = "Test Gıda A.Ş." },
            Modules = modules.Length > 0 ? modules : [PaidModule.Id],
            Stations = stations,
            IssuedAt = DateTimeOffset.UtcNow,
            ValidFrom = Today.AddDays(-10),
            ValidUntil = validUntil ?? Today.AddDays(365),
            GraceDays = graceDays,
        };

    public string Sign(LicensePayload payload) => new LicenseSigner(_key, KeyId).Sign(payload);

    /// <summary>A file signed by a key the application does not trust, under the trusted key id.</summary>
    public string SignWithOtherKey(LicensePayload payload) => new LicenseSigner(_otherKey, KeyId).Sign(payload);

    /// <summary>A file signed with a key id the application does not know.</summary>
    public string SignWithUnknownKeyId(LicensePayload payload) => new LicenseSigner(_key, "unknown-key").Sign(payload);

    /// <summary>The file of <paramref name="original"/> with the payload of <paramref name="changed"/>: the signature no longer matches.</summary>
    public string Tampered(LicensePayload original, LicensePayload changed)
    {
        using var originalFile = JsonDocument.Parse(Sign(original));
        using var changedFile = JsonDocument.Parse(Sign(changed));
        var envelope = originalFile.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        envelope["payload"] = changedFile.RootElement.GetProperty("payload").GetString();
        return JsonSerializer.Serialize(envelope);
    }

    public void Dispose()
    {
        _key.Dispose();
        _otherKey.Dispose();
    }
}

/// <summary>
/// The real host with the sample modules (Orders, Notifications, Paid) on a test database, with a movable clock, the
/// test issuer's key ring, and the background outbox poll slowed down so that tests deliver events themselves.
/// </summary>
internal sealed class LicenseTestApp : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    /// <param name="database">The test database.</param>
    /// <param name="issuer">The issuer whose key is trusted.</param>
    /// <param name="clock">The clock; a new one when null.</param>
    /// <param name="maintenance">
    /// False removes <see cref="LicenseMaintenanceService"/>, so that the latest seen time is written only when the test
    /// calls <see cref="PersistAsync"/>.
    /// </param>
    public LicenseTestApp(TestDatabase database, TestLicenseIssuer issuer, TestClock? clock = null, bool maintenance = true)
    {
        Clock = clock ?? new TestClock();
        var dataDirectory = database.DataDirectory;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, dataDirectory);
            builder.UseSetting(DatabaseSettings.ProviderKey, database.Provider.ToString());
            builder.UseSetting(DatabaseSettings.ConnectionStringKey, database.ConnectionString);
            builder.UseSetting("Vyxum:Modules:0", "Vyxum.Samples.Orders");
            builder.UseSetting("Vyxum:Modules:1", "Vyxum.Samples.Notifications");
            builder.UseSetting("Vyxum:Modules:2", "Vyxum.Samples.Paid");
            builder.UseSetting($"{OutboxOptions.SectionName}:PollingInterval", "01:00:00");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<ILicenseKeyRing>(issuer.KeyRing);
                if (!maintenance)
                {
                    foreach (var service in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(LicenseMaintenanceService)).ToList())
                    {
                        services.Remove(service);
                    }
                }
            });
        });
        Client = _factory.CreateClient();
    }

    public TestClock Clock { get; }

    public HttpClient Client { get; }

    public IServiceProvider Services => _factory.Services;

    public TestServer Server => _factory.Server;

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public Task<HttpResponseMessage> UploadAsync(string file) =>
        Client.PostAsync(LicenseEndpointExtensions.LicensePath, new StringContent(file, Encoding.UTF8, "application/octet-stream"), TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> UploadAsFormAsync(string file)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(file));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", "customer.vxlic");
        return Client.PostAsync(LicenseEndpointExtensions.LicensePath, form, TestContext.Current.CancellationToken);
    }

    public async Task<(int Status, JsonElement Body)> SendAsync(HttpMethod method, string path, string? json = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = Json(json);
        }

        using var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        return await ReadAsync(response);
    }

    public static async Task<(int Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(text.Length == 0 ? "null" : text);
        return ((int)response.StatusCode, document.RootElement.Clone());
    }

    public Task<(int Status, JsonElement Body)> GetLicenseAsync() => SendAsync(HttpMethod.Get, LicenseEndpointExtensions.LicensePath);

    public async Task CreateDeviceAsync(string id, bool enabled = true)
    {
        var json = $$"""{ "id": "{{id}}", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" }, "isEnabled": {{(enabled ? "true" : "false")}} }""";
        var (status, _) = await SendAsync(HttpMethod.Post, DeviceEndpointExtensions.ConfigPath, json);
        Assert.Equal(201, status);
    }

    public LicenseManager Licenses => Services.GetRequiredService<LicenseManager>();

    /// <summary>Writes the latest seen time as the maintenance service does.</summary>
    public Task PersistAsync() => Licenses.PersistLastSeenAsync(TestContext.Current.CancellationToken);

    /// <summary>The stored latest seen time of <paramref name="licenseId"/>, or null.</summary>
    public async Task<DateTimeOffset?> StoredLastSeenAsync(string licenseId = "lic-1")
    {
        await using var scope = Services.CreateAsyncScope();
        var key = LicenseManager.LastSeenKey(licenseId);
        var value = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Settings.AsNoTracking()
            .Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        return value is null ? null : DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task<int> DispatchAsync() =>
        Services.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}
