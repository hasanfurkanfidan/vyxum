using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.FileProviders;
using Shouldly;
using Vyxum.Platform;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>Serves a temporary web root, so the tests do not need the built UI.</summary>
public sealed class StaticFrontendTests : IDisposable
{
    private const string IndexHtml = "<!doctype html><title>test-ui</title>";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vyxum-web-" + Guid.NewGuid().ToString("N"));

    private string DataDirectory => Path.Combine(_root, "data");

    private string WebRoot => Path.Combine(_root, "wwwroot");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly; the temp folder is disposable anyway.
        }
    }

    private WebApplicationFactory<Program> CreateFactory(bool withIndex)
    {
        Directory.CreateDirectory(WebRoot);
        if (withIndex)
        {
            File.WriteAllText(Path.Combine(WebRoot, "index.html"), IndexHtml);
            Directory.CreateDirectory(Path.Combine(WebRoot, "assets"));
            File.WriteAllText(Path.Combine(WebRoot, "assets", "app.js"), "export const x = 1;");
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, DataDirectory);
            builder.ConfigureServices(services =>
            {
                // The web root is fixed before the factory can override it, so point the environment at the temporary folder.
                var environment = (IWebHostEnvironment)services.Last(d => d.ServiceType == typeof(IWebHostEnvironment)).ImplementationInstance!;
                environment.WebRootPath = WebRoot;
                environment.WebRootFileProvider = new PhysicalFileProvider(WebRoot);
            });
        });
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/products")]
    [InlineData("/weighing/some/deep/route")]
    public async Task Frontend_paths_return_index_html(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(withIndex: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(ct)).ShouldBe(IndexHtml);
    }

    [Fact]
    public async Task Built_asset_files_are_served_as_themselves_and_not_as_index_html()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(withIndex: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/assets/app.js", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldNotBeNull().ShouldContain("javascript");
        (await response.Content.ReadAsStringAsync(ct)).ShouldBe("export const x = 1;");
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/api")]
    [InlineData("/hubs/unknown")]
    [InlineData("/health/unknown")]
    public async Task Unknown_backend_paths_return_404_and_not_the_frontend(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(withIndex: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(ct)).ShouldNotContain("test-ui");
    }

    [Fact]
    public async Task Backend_endpoints_still_win_over_the_fallback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(withIndex: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(ct)).ShouldContain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task Root_returns_404_when_the_ui_is_not_built()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(withIndex: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
