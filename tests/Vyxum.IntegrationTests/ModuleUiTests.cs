using System.Net;
using System.Text.Json;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Testing;
using Vyxum.Samples.Paid;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>
/// The embedded screens of a module (phase F4, step 4.3): <c>/modules/{id}/{path}</c> under the license rules and the
/// <c>ui</c> entry of <c>/api/ui/modules</c>, on both providers.
/// </summary>
public sealed class ModuleUiTests : IDisposable
{
    private const string Entry = "/modules/" + PaidModule.Id + "/index.js";

    private readonly TestLicenseIssuer _issuer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _issuer.Dispose();

    private async Task<LicenseTestApp> StartLicensedAsync(TestDatabase database, DateOnly? validUntil = null)
    {
        var app = new LicenseTestApp(database, _issuer);
        using var response = await app.UploadAsync(_issuer.Sign(TestLicenseIssuer.Payload(validUntil: validUntil)));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return app;
    }

    private static async Task<JsonElement> PaidModuleAsync(LicenseTestApp app)
    {
        var (status, body) = await app.SendAsync(HttpMethod.Get, UiEndpointExtensions.ModulesPath);
        status.ShouldBe(200);
        return body.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("id").GetString() == PaidModule.Id);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Without_a_license_the_screens_are_not_served_or_offered(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = new LicenseTestApp(database, _issuer);

        var (status, body) = await app.SendAsync(HttpMethod.Get, Entry);
        status.ShouldBe(403);
        body.GetProperty("code").GetString().ShouldBe("license_required");
        using var head = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, Entry), Ct);
        head.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var module = await PaidModuleAsync(app);
        module.GetProperty("license").GetString().ShouldBe("locked");
        module.GetProperty("ui").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task With_a_license_the_entry_is_served_as_javascript_and_offered(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = await StartLicensedAsync(database);

        using var response = await app.Client.GetAsync(Entry, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("text/javascript");
        response.Headers.CacheControl.ShouldNotBeNull().NoCache.ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        var etag = response.Headers.ETag.ShouldNotBeNull();
        var text = await response.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("export function register(host)");
        text.ShouldContain("from 'react'");

        // An unchanged file is revalidated, not downloaded again.
        using var revalidate = new HttpRequestMessage(HttpMethod.Get, Entry);
        revalidate.Headers.IfNoneMatch.Add(etag);
        using var notModified = await app.Client.SendAsync(revalidate, Ct);
        notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);

        var ui = (await PaidModuleAsync(app)).GetProperty("ui");
        ui.GetProperty("entry").GetString().ShouldBe(Entry);
        ui.GetProperty("sdkVersion").GetInt32().ShouldBe(1);

        // Removing the license locks the screens again at once.
        (await app.SendAsync(HttpMethod.Delete, LicenseEndpointExtensions.LicensePath)).Status.ShouldBeOneOf(200, 204);
        using var revalidateLocked = new HttpRequestMessage(HttpMethod.Get, Entry);
        revalidateLocked.Headers.IfNoneMatch.Add(etag);
        using var locked = await app.Client.SendAsync(revalidateLocked, Ct);
        locked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_read_only_module_still_serves_its_screens(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = await StartLicensedAsync(database, validUntil: TestLicenseIssuer.Today.AddDays(1));
        app.Clock.Advance(TimeSpan.FromDays(40));

        var module = await PaidModuleAsync(app);
        module.GetProperty("license").GetString().ShouldBe("readOnly");
        module.GetProperty("ui").GetProperty("entry").GetString().ShouldBe(Entry);
        using var response = await app.Client.GetAsync(Entry, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("text/javascript");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Unknown_files_modules_and_traversal_attempts_return_404(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = await StartLicensedAsync(database);

        string[] paths =
        [
            "/modules/" + PaidModule.Id + "/missing.js",
            "/modules/" + PaidModule.Id + "/",
            "/modules/" + PaidModule.Id + "/ui/index.js",
            "/modules/" + PaidModule.Id + "/..%2F..%2Fappsettings.json",
            "/modules/" + PaidModule.Id + "/%2e%2e/index.js",
            "/modules/" + PaidModule.Id + "/.%2Findex.js",
            "/modules/" + PaidModule.Id + "/..%5Cindex.js",
            "/modules/" + PaidModule.Id + "/Vyxum.Samples.Paid.Resources.Strings.resources",
            "/modules/unknown/index.js",
            "/modules/products/index.js",
        ];
        foreach (var path in paths)
        {
            using var response = await app.Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/html", path);
        }

        // Writes are not mapped at all.
        using var post = await app.Client.PostAsync(Entry, new StringContent(""), Ct);
        ((int)post.StatusCode).ShouldBeOneOf(404, 405);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Free_modules_without_screens_have_no_ui_entry(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = new LicenseTestApp(database, _issuer);

        var (_, body) = await app.SendAsync(HttpMethod.Get, UiEndpointExtensions.ModulesPath);
        var products = body.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "products");
        products.GetProperty("ui").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
