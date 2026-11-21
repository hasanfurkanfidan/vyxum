using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Vyxum.Platform;
using Xunit;

namespace Vyxum.IntegrationTests;

public sealed class HostTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-host-" + Guid.NewGuid().ToString("N"));

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

    private WebApplicationFactory<Program> CreateFactory(params string[] moduleAssemblies) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory);
            for (var i = 0; i < moduleAssemblies.Length; i++)
            {
                builder.UseSetting($"Vyxum:Modules:{i}", moduleAssemblies[i]);
            }
        });

    [Fact]
    public async Task Host_boots_with_zero_config_on_sqlite_and_reports_health()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory("Vyxum.Samples.Orders", "Vyxum.Samples.Notifications");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().ShouldBe("ok");
        json.RootElement.GetProperty("database").GetString().ShouldBe("Sqlite");
        json.RootElement.GetProperty("modules").EnumerateArray().Select(m => m.GetProperty("id").GetString())
            .ShouldBe(["products", "samples.notifications", "samples.orders"], ignoreOrder: true);
        body.ShouldNotContain(_dataDirectory);
        File.Exists(Path.Combine(_dataDirectory, DatabaseSettings.DefaultSqliteFileName)).ShouldBeTrue();
    }

    [Fact]
    public async Task Host_without_configured_modules_loads_only_the_built_in_ones()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        json.RootElement.GetProperty("modules").EnumerateArray().Select(m => m.GetProperty("id").GetString())
            .ShouldBe(["products"]);
    }

    [Theory]
    [InlineData(null, "Vyxum'dan merhaba")]
    [InlineData("en", "Hello from Vyxum")]
    public async Task Hello_is_localized_with_turkish_as_default(string? language, string expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/hello");
        if (language is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        var response = await client.SendAsync(request, ct);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        json.RootElement.GetProperty("message").GetString().ShouldBe(expected);
    }

    [Fact]
    public void Host_fails_to_start_naming_the_missing_required_capability()
    {
        // This assembly holds MissingCapabilityModule, which requires a capability nobody provides.
        using var factory = CreateFactory("Vyxum.IntegrationTests");

        var exception = Should.Throw<ModuleValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("tests.needs-missing");
        exception.Message.ShouldContain("tests.missing-capability");
    }
}
