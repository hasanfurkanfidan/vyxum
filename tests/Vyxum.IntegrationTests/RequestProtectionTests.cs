using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Vyxum.Platform;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>Web pages open in a browser on the weighing computer must not change data or reach the API through DNS rebinding.</summary>
public sealed class RequestProtectionTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-protect-" + Guid.NewGuid().ToString("N"));

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
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory);
            builder.UseSetting(RequestProtection.AllowedHostsKey, "tartim.firma.local");
        });

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpMethod method, string url, Action<HttpRequestMessage> configure)
    {
        using var request = new HttpRequestMessage(method, url);
        configure(request);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Theory]
    [InlineData("/api/devices/scale-1/zero")]
    [InlineData("/api/products/0e7d3f7a-5d0e-4a39-9f0e-000000000417/deactivate")]
    [InlineData("/api/products/0e7d3f7a-5d0e-4a39-9f0e-000000000417/activate")]
    public async Task Data_changes_sent_by_a_page_of_another_site_are_refused(string url)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        (await SendAsync(client, HttpMethod.Post, url, r => r.Headers.Add("Origin", "https://evil.example"))).ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(client, HttpMethod.Post, url, r => r.Headers.Add("Sec-Fetch-Site", "cross-site"))).ShouldBe(HttpStatusCode.Forbidden);

        // The application's own page, and callers that are not browsers, pass the check.
        (await SendAsync(client, HttpMethod.Post, url, r => r.Headers.Add("Origin", "http://localhost"))).ShouldNotBe(HttpStatusCode.Forbidden);
        (await SendAsync(client, HttpMethod.Post, url, r => r.Headers.Add("Sec-Fetch-Site", "same-origin"))).ShouldNotBe(HttpStatusCode.Forbidden);
        (await SendAsync(client, HttpMethod.Post, url, _ => { })).ShouldNotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_host_names_are_refused()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        (await SendAsync(client, HttpMethod.Get, "/api/setup/status", r => r.Headers.Host = "rebind.evil.example")).ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(client, HttpMethod.Get, "/health", r => r.Headers.Host = "rebind.evil.example:5000")).ShouldBe(HttpStatusCode.BadRequest);

        (await SendAsync(client, HttpMethod.Get, "/health", r => r.Headers.Host = "localhost:5000")).ShouldBe(HttpStatusCode.OK);
        (await SendAsync(client, HttpMethod.Get, "/health", r => r.Headers.Host = "192.168.1.20:5000")).ShouldBe(HttpStatusCode.OK);
        (await SendAsync(client, HttpMethod.Get, "/health", r => r.Headers.Host = "tartim.firma.local")).ShouldBe(HttpStatusCode.OK);
        (await SendAsync(client, HttpMethod.Get, "/health", r => r.Headers.Host = Environment.MachineName)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_agent_hub_refuses_a_web_page()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        (await SendAsync(client, HttpMethod.Post, AgentHubContract.Path + "/negotiate?negotiateVersion=1", r => r.Headers.Add("Origin", "http://localhost")))
            .ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(client, HttpMethod.Post, AgentHubContract.Path + "/negotiate?negotiateVersion=1", _ => { }))
            .ShouldBe(HttpStatusCode.OK);
    }
}
