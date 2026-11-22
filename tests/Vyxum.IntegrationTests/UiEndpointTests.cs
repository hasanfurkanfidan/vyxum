using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Vyxum.Platform;
using Xunit;

namespace Vyxum.IntegrationTests;

public sealed class UiEndpointTests : IDisposable
{
    private const string CultureCookie = ".AspNetCore.Culture";

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-ui-" + Guid.NewGuid().ToString("N"));

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

    private static async Task<JsonElement> GetStringsAsync(HttpClient client, string? acceptLanguage, string? cookieCulture)
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Get, UiEndpointExtensions.StringsPath);
        if (acceptLanguage is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);
        }

        if (cookieCulture is not null)
        {
            request.Headers.Add("Cookie", $"{CultureCookie}={Uri.EscapeDataString($"c={cookieCulture}|uic={cookieCulture}")}");
        }

        using var response = await client.SendAsync(request, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.Clone();
    }

    [Theory]
    [InlineData(null, null, "tr", "Tartım", "Bağlı terazi yok")]
    [InlineData("en", null, "en", "Weighing", "No scale connected")]
    [InlineData("en-US", null, "en", "Weighing", "No scale connected")]
    [InlineData("de", null, "tr", "Tartım", "Bağlı terazi yok")]
    [InlineData(null, "en", "en", "Weighing", "No scale connected")]
    [InlineData("en", "tr", "tr", "Tartım", "Bağlı terazi yok")]
    [InlineData("tr", "en", "en", "Weighing", "No scale connected")]
    public async Task Strings_follow_the_culture_cookie_then_accept_language_with_turkish_default(
        string? acceptLanguage, string? cookieCulture, string expectedCulture, string weighing, string noDevice)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var body = await GetStringsAsync(client, acceptLanguage, cookieCulture);

        body.GetProperty("culture").GetString().ShouldBe(expectedCulture);
        var strings = body.GetProperty("strings");
        strings.GetProperty("shell.Weighing").GetString().ShouldBe(weighing);
        strings.GetProperty("shell.NoDevice").GetString().ShouldBe(noDevice);
        strings.GetProperty("shell.AppName").GetString().ShouldBe("Vyxum");
    }

    [Fact]
    public async Task Both_cultures_serve_the_same_non_empty_keys()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var tr = (await GetStringsAsync(client, "tr", null)).GetProperty("strings").EnumerateObject().ToList();
        var en = (await GetStringsAsync(client, "en", null)).GetProperty("strings").EnumerateObject().ToList();

        tr.Select(p => p.Name).ShouldBe(en.Select(p => p.Name));
        tr.ShouldAllBe(p => (p.Name.StartsWith("shell.") || p.Name.StartsWith("setup.") || p.Name.StartsWith("devices.") || p.Name.StartsWith("license.") || p.Name.StartsWith("products.") || p.Name.StartsWith("weighing.") || p.Name.StartsWith("glossary.")) && !string.IsNullOrWhiteSpace(p.Value.GetString()));
        en.ShouldAllBe(p => !string.IsNullOrWhiteSpace(p.Value.GetString()));
    }

    [Fact]
    public async Task Modules_endpoint_lists_loaded_modules_and_capabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory("Vyxum.Samples.Orders", "Vyxum.Samples.Notifications");
        using var client = factory.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync(UiEndpointExtensions.ModulesPath, ct));

        var modules = json.RootElement.GetProperty("modules").EnumerateArray()
            .Select(m => (m.GetProperty("id").GetString(), m.GetProperty("version").GetString()))
            .ToList();
        modules.ShouldBe([("products", "1.0"), ("samples.notifications", "1.0"), ("samples.orders", "1.0"), ("weighing", "1.0")]);
        json.RootElement.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString())
            .ShouldBe(["products.catalog", "samples.orders", "weighing.records"], ignoreOrder: true);
    }

    [Fact]
    public async Task Modules_endpoint_lists_the_built_in_modules()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync(UiEndpointExtensions.ModulesPath, ct));

        json.RootElement.GetProperty("modules").EnumerateArray().Select(m => m.GetProperty("id").GetString())
            .ShouldBe(["products", "weighing"]);
        json.RootElement.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString())
            .ShouldBe(["products.catalog", "weighing.records"]);
    }

    [Theory]
    [InlineData("tr", "Tartım", "Ürünler")]
    [InlineData("en", "Weighing", "Products")]
    public async Task Modules_endpoint_serves_the_display_names_of_the_current_culture(string culture, string weighing, string products)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, UiEndpointExtensions.ModulesPath);
        request.Headers.AcceptLanguage.ParseAdd(culture);
        using var response = await client.SendAsync(request, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var modules = json.RootElement.GetProperty("modules").EnumerateArray()
            .ToDictionary(m => m.GetProperty("id").GetString()!, m => m);
        modules["weighing"].GetProperty("name").GetString().ShouldBe(weighing);
        modules["products"].GetProperty("name").GetString().ShouldBe(products);
    }

    [Fact]
    public async Task Modules_endpoint_lists_provided_required_and_optional_capabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync(UiEndpointExtensions.ModulesPath, ct));

        var weighing = json.RootElement.GetProperty("modules").EnumerateArray()
            .Single(m => m.GetProperty("id").GetString() == "weighing");
        weighing.GetProperty("provides").EnumerateArray().Select(c => c.GetString()).ShouldBe(["weighing.records"]);
        weighing.GetProperty("requires").GetArrayLength().ShouldBe(0);
        weighing.GetProperty("optional").EnumerateArray().Select(c => c.GetString())
            .ShouldBe(["products.catalog", "labels.print", "identity.users"], ignoreOrder: true);
    }
}
