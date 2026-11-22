using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Vyxum.Platform;
using Xunit;

namespace Vyxum.IntegrationTests;

public sealed class GlossaryApiTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-glossary-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Nothing was written, or SQLite still holds the file briefly; the temp folder is disposable anyway.
        }
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory));

    private static async Task<JsonElement> GetAsync(HttpClient client, string acceptLanguage)
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Get, GlossaryEndpointExtensions.GlossaryPath);
        request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);
        using var response = await client.SendAsync(request, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.Clone();
    }

    [Theory]
    [InlineData("tr", "tr", "Dara", "Tare", "en")]
    [InlineData("en", "en", "Tare", "Dara", "tr")]
    public async Task The_glossary_is_served_in_the_request_culture_with_the_other_name(
        string acceptLanguage, string culture, string name, string otherName, string otherCulture)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var body = await GetAsync(client, acceptLanguage);

        body.GetProperty("culture").GetString().ShouldBe(culture);
        var sections = body.GetProperty("sections").EnumerateArray().ToList();
        sections.Count.ShouldBe(9);
        var tare = sections[1].GetProperty("terms").EnumerateArray().Single(t => t.GetProperty("id").GetString() == "T1.04");
        tare.GetProperty("name").GetString().ShouldBe(name);
        tare.GetProperty("text").GetString().ShouldNotBeNullOrWhiteSpace();
        var other = tare.GetProperty("otherNames").EnumerateArray().Single();
        other.GetProperty("culture").GetString().ShouldBe(otherCulture);
        other.GetProperty("name").GetString().ShouldBe(otherName);
    }
}
