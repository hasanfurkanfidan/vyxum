using System.Net.Http.Headers;
using System.Text;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>The state-changing license endpoints accept only requests the web application itself sends.</summary>
public sealed class LicenseCsrfTests : IDisposable
{
    private readonly TestLicenseIssuer _issuer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _issuer.Dispose();

    private static async Task<(int Status, string? Code)> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request, Ct);
        var (status, body) = await LicenseTestApp.ReadAsync(response);
        return (status, body.ValueKind == System.Text.Json.JsonValueKind.Object && body.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content = null, bool header = false, string? origin = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (header)
        {
            request.Headers.Add(SameOriginRequestFilter.HeaderName, SameOriginRequestFilter.HeaderValue);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    private static MultipartFormDataContent Form(string file)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(file));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", "customer.vxlic");
        return form;
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Simple_cross_site_requests_are_rejected(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = new LicenseTestApp(database, _issuer, maintenance: false);
        using var client = app.CreatePlainClient();
        var file = _issuer.Sign(TestLicenseIssuer.Payload());

        // What a page on another site can send without a preflight: text/plain, multipart, a form post; DELETE and PUT
        // without the header.
        (await SendAsync(client, Request(HttpMethod.Post, LicenseEndpointExtensions.LicensePath, new StringContent(file, Encoding.UTF8, "text/plain"))))
            .ShouldBe((403, SameOriginRequestFilter.HeaderRequired));
        (await SendAsync(client, Request(HttpMethod.Post, LicenseEndpointExtensions.LicensePath, Form(file))))
            .ShouldBe((403, SameOriginRequestFilter.HeaderRequired));
        (await SendAsync(client, Request(HttpMethod.Post, LicenseEndpointExtensions.LicensePath, new FormUrlEncodedContent([KeyValuePair.Create("a", file)]))))
            .ShouldBe((403, SameOriginRequestFilter.HeaderRequired));
        (await SendAsync(client, Request(HttpMethod.Delete, LicenseEndpointExtensions.LicensePath)))
            .ShouldBe((403, SameOriginRequestFilter.HeaderRequired));

        // The station endpoint binds JSON, so routing already turns other bodies away.
        (await SendAsync(client, Request(HttpMethod.Put, LicenseEndpointExtensions.StationsPath, new StringContent("""{ "deviceIds": [] }""", Encoding.UTF8, "text/plain"))))
            .Status.ShouldBe(415);

        // A foreign Origin is rejected even with the header or a JSON body.
        (await SendAsync(client, Request(HttpMethod.Post, LicenseEndpointExtensions.LicensePath, new StringContent(file, Encoding.UTF8, "text/plain"), header: true, origin: "http://evil.example")))
            .ShouldBe((403, SameOriginRequestFilter.CrossOrigin));
        (await SendAsync(client, Request(HttpMethod.Put, LicenseEndpointExtensions.StationsPath, LicenseTestApp.Json("""{ "deviceIds": [] }"""), origin: "http://localhost:5173")))
            .ShouldBe((403, SameOriginRequestFilter.CrossOrigin));
        (await SendAsync(client, Request(HttpMethod.Delete, LicenseEndpointExtensions.LicensePath, header: true, origin: "null")))
            .ShouldBe((403, SameOriginRequestFilter.CrossOrigin));

        (await app.GetLicenseAsync()).Body.GetProperty("hasLicense").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Requests_of_the_web_application_pass(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = new LicenseTestApp(database, _issuer, maintenance: false);
        using var client = app.CreatePlainClient();
        var file = _issuer.Sign(TestLicenseIssuer.Payload());

        // The header with any body; the same origin; reading needs nothing.
        (await SendAsync(client, Request(HttpMethod.Post, LicenseEndpointExtensions.LicensePath, new StringContent(file, Encoding.UTF8, "text/plain"), header: true, origin: "http://localhost"))).Status.ShouldBe(200);
        (await SendAsync(client, Request(HttpMethod.Post, LicenseEndpointExtensions.LicensePath, Form(file), header: true))).Status.ShouldBe(200);
        (await SendAsync(client, Request(HttpMethod.Get, LicenseEndpointExtensions.LicensePath, origin: "http://evil.example"))).Status.ShouldBe(200);

        // A JSON body needs a preflight cross-site, so it passes without the header.
        (await SendAsync(client, Request(HttpMethod.Put, LicenseEndpointExtensions.StationsPath, LicenseTestApp.Json("""{ "deviceIds": [] }""")))).Status.ShouldBe(200);
        (await SendAsync(client, Request(HttpMethod.Delete, LicenseEndpointExtensions.LicensePath, header: true))).Status.ShouldBe(204);
    }
}
