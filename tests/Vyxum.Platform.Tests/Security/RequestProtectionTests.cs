using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Vyxum.Platform.Tests.Security;

public class RequestProtectionTests
{
    private static readonly HashSet<string> Names = new(["PC-1", "pc-1.firma.local", "tartim.firma.local"], StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("app.localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.20")]
    [InlineData("[::1]")]
    [InlineData("pc-1")]
    [InlineData("PC-1.firma.local")]
    [InlineData("tartim.firma.local.")]
    [InlineData("")]
    [InlineData(null)]
    public void Ip_addresses_localhost_and_known_names_are_allowed_hosts(string? host) =>
        RequestProtection.IsAllowedHost(host, Names).ShouldBeTrue();

    [Theory]
    [InlineData("evil.example")]
    [InlineData("localhost.evil.example")]
    [InlineData("127.0.0.1.nip.io")]
    [InlineData("pc-2")]
    public void Other_names_are_not_allowed_hosts(string host) =>
        RequestProtection.IsAllowedHost(host, Names).ShouldBeFalse();

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("same-origin", null, false)]
    [InlineData("none", null, false)]
    [InlineData("cross-site", null, true)]
    [InlineData("same-site", null, true)]
    [InlineData(null, "http://localhost:5000", false)]
    [InlineData(null, "http://LOCALHOST:5000", false)]
    [InlineData(null, "http://localhost:5173", true)]
    [InlineData(null, "https://evil.example", true)]
    [InlineData(null, "null", true)]
    [InlineData("same-origin", "http://localhost:5173", false)]
    public void A_request_is_cross_site_when_the_browser_says_so(string? fetchSite, string? origin, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("localhost:5000");
        if (fetchSite is not null)
        {
            context.Request.Headers["Sec-Fetch-Site"] = fetchSite;
        }

        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        RequestProtection.IsCrossSite(context.Request).ShouldBe(expected);
    }

    [Fact]
    public void Allowed_names_include_this_computer_and_the_configured_names()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [RequestProtection.AllowedHostsKey] = " tartim.firma.local ; scale-pc. " })
            .Build();

        var names = RequestProtection.AllowedNames(configuration);

        names.ShouldContain(Environment.MachineName);
        names.ShouldContain("TARTIM.firma.local");
        names.ShouldContain("scale-pc");
    }
}
