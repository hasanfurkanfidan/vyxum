using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Vyxum.Platform.Tests.Devices;

public class AgentConnectionGateTests
{
    private static AgentConnectionGate Gate(string? key) =>
        new(new FixedOptions(new AgentOptions { Key = key }), NullLogger<AgentConnectionGate>.Instance);

    private static DefaultHttpContext Request(string? address, string? header = null, string? query = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
        if (header is not null)
        {
            context.Request.Headers[AgentHubContract.KeyHeader] = header;
        }

        if (query is not null)
        {
            context.Request.QueryString = QueryString.Create("access_token", query);
        }

        return context;
    }

    [Theory]
    [InlineData("127.0.0.1", AgentConnectionDecision.Allowed)]
    [InlineData("127.10.0.1", AgentConnectionDecision.Allowed)]
    [InlineData("::1", AgentConnectionDecision.Allowed)]
    [InlineData("::ffff:127.0.0.1", AgentConnectionDecision.Allowed)]
    [InlineData(null, AgentConnectionDecision.Allowed)]
    [InlineData("192.168.1.50", AgentConnectionDecision.RemoteNotAllowed)]
    [InlineData("10.0.0.1", AgentConnectionDecision.RemoteNotAllowed)]
    [InlineData("::ffff:192.168.1.50", AgentConnectionDecision.RemoteNotAllowed)]
    [InlineData("fe80::1", AgentConnectionDecision.RemoteNotAllowed)]
    public void Without_key_only_local_addresses_are_allowed(string? address, AgentConnectionDecision expected)
    {
        Gate(null).Check(Request(address)).ShouldBe(expected);
        Gate("  ").Check(Request(address)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("secret", null, AgentConnectionDecision.Allowed)]
    [InlineData("secret", "wrong", AgentConnectionDecision.Allowed)]
    [InlineData(null, "secret", AgentConnectionDecision.InvalidKey)]
    [InlineData(null, null, AgentConnectionDecision.InvalidKey)]
    [InlineData("wrong", null, AgentConnectionDecision.InvalidKey)]
    [InlineData("secre", null, AgentConnectionDecision.InvalidKey)]
    [InlineData("SECRET", null, AgentConnectionDecision.InvalidKey)]
    [InlineData(null, "wrong", AgentConnectionDecision.InvalidKey)]
    public void With_key_the_header_must_match_and_the_query_is_ignored(string? header, string? query, AgentConnectionDecision expected)
    {
        Gate("secret").Check(Request("192.168.1.50", header, query)).ShouldBe(expected);
    }

    [Fact]
    public void With_key_loopback_must_send_it_too()
    {
        Gate("secret").Check(Request("127.0.0.1")).ShouldBe(AgentConnectionDecision.InvalidKey);
    }

    [Fact]
    public void Admit_sets_status_codes()
    {
        var unauthorized = Request("127.0.0.1");
        Gate("secret").Admit(unauthorized).ShouldBeFalse();
        unauthorized.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);

        var forbidden = Request("192.168.1.50");
        Gate(null).Admit(forbidden).ShouldBeFalse();
        forbidden.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);

        Gate(null).Admit(Request("127.0.0.1")).ShouldBeTrue();
    }

    private sealed class FixedOptions(AgentOptions value) : IOptionsMonitor<AgentOptions>
    {
        public AgentOptions CurrentValue => value;

        public AgentOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AgentOptions, string?> listener) => null;
    }
}
