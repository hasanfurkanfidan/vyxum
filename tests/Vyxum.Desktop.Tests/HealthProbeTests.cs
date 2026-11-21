using System.Net;
using Shouldly;
using Xunit;
using Vyxum.Desktop;

namespace Vyxum.Desktop.Tests;

public sealed class HealthProbeTests
{
    private static readonly Uri Base = new("http://localhost:5000/");
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(5);

    [Fact]
    public async Task Succeeds_after_the_server_comes_up()
    {
        var handler = new FakeHandler(call => call < 4 ? throw new HttpRequestException("refused") : HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        var result = await HealthProbe.WaitAsync(client, Base, Interval, TimeSpan.FromSeconds(10), CancellationToken.None);

        result.ShouldBeTrue();
        handler.Calls.ShouldBe(4);
        handler.LastUri.ShouldBe(new Uri("http://localhost:5000/health"));
    }

    [Fact]
    public async Task Server_error_counts_as_not_ready()
    {
        var handler = new FakeHandler(call => call < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        (await HealthProbe.WaitAsync(client, Base, Interval, TimeSpan.FromSeconds(10), CancellationToken.None)).ShouldBeTrue();
        handler.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task Gives_up_after_the_timeout()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("refused"));
        using var client = new HttpClient(handler);

        var result = await HealthProbe.WaitAsync(client, Base, Interval, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        result.ShouldBeFalse();
        handler.Calls.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Request_timeout_is_retried()
    {
        var handler = new FakeHandler(call => call < 2 ? throw new TaskCanceledException("timeout") : HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        (await HealthProbe.WaitAsync(client, Base, Interval, TimeSpan.FromSeconds(10), CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Cancellation_stops_the_wait()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("refused"));
        using var client = new HttpClient(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(
            () => HealthProbe.WaitAsync(client, Base, Interval, TimeSpan.FromSeconds(30), cts.Token));
    }

    private sealed class FakeHandler(Func<int, HttpStatusCode> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(respond(Calls)));
        }
    }
}
