using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Shouldly;
using Xunit;

namespace Vyxum.Devices.Tests;

public class DeviceRunnerTests
{
    private static readonly DeviceRunnerOptions FastOptions = new()
    {
        InitialReconnectDelay = TimeSpan.FromMilliseconds(2),
        MaxReconnectDelay = TimeSpan.FromMilliseconds(8),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reopens_after_open_failure_and_reports_status_transitions()
    {
        var transports = new List<FakeTransport>();
        var protocol = new ScriptedProtocol(YieldThenWaitAsync);
        var runner = new DeviceRunner("scale-1", () => Add(transports, new FakeTransport
        {
            OpenException = transports.Count == 0 ? new IOException("Port is busy.") : null,
        }), protocol, FastOptions);
        var statuses = Record(runner);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = runner.RunAsync(cts.Token);
        var reading = await runner.Readings.ReadAsync(Ct);
        await cts.CancelAsync();
        await run;

        reading.Value.ShouldBe(572m);
        transports.Count.ShouldBe(2);
        transports.ShouldAllBe(t => t.CloseCount == 1 && t.DisposeCount == 1);
        statuses.Select(s => s.Status).ShouldBe(
        [
            DeviceStatus.Connecting,
            DeviceStatus.Disconnected,
            DeviceStatus.Connecting,
            DeviceStatus.Connected,
            DeviceStatus.Disconnected,
        ]);
        statuses.ToArray()[1].LastError.ShouldBe("Port is busy.");
        statuses.ToArray()[1].RetryIn.ShouldBe(TimeSpan.FromMilliseconds(2));
        statuses.ToArray()[^1].LastError.ShouldBeNull();
        statuses.ShouldAllBe(s => s.DeviceId == "scale-1");
        runner.Status.Status.ShouldBe(DeviceStatus.Disconnected);
    }

    [Fact]
    public async Task Reopens_after_connection_lost_and_keeps_forwarding_readings()
    {
        var transports = new List<FakeTransport>();
        var protocol = new ScriptedProtocol(YieldThenLoseAsync, YieldThenLoseAsync, YieldThenWaitAsync);
        var runner = new DeviceRunner("scale-1", () => Add(transports, new FakeTransport()), protocol, FastOptions);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = runner.RunAsync(cts.Token);
        for (var i = 0; i < 3; i++)
        {
            await runner.Readings.ReadAsync(Ct);
        }

        await cts.CancelAsync();
        await run;

        transports.Count.ShouldBe(3);
        transports.ShouldAllBe(t => t.OpenCount == 1 && t.CloseCount == 1 && t.DisposeCount == 1);
    }

    [Fact]
    public async Task Backoff_grows_to_the_maximum_and_resets_after_a_reading()
    {
        var opens = 0;
        var protocol = new ScriptedProtocol(YieldThenLoseAsync, YieldThenWaitAsync);
        var runner = new DeviceRunner(
            "scale-1",
            () => new FakeTransport { OpenException = ++opens <= 4 ? new IOException("No device.") : null },
            protocol,
            FastOptions);
        var statuses = Record(runner);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = runner.RunAsync(cts.Token);
        await runner.Readings.ReadAsync(Ct);
        await runner.Readings.ReadAsync(Ct);
        await cts.CancelAsync();
        await run;

        statuses.Where(s => s.RetryIn is not null).Select(s => s.RetryIn!.Value.TotalMilliseconds)
            .ShouldBe([2, 4, 8, 8, 2]);
    }

    [Fact]
    public async Task Protocol_that_ends_without_error_is_treated_as_a_failure()
    {
        var protocol = new ScriptedProtocol(EndAsync, YieldThenWaitAsync);
        var runner = new DeviceRunner("scale-1", () => new FakeTransport(), protocol, FastOptions);
        var statuses = Record(runner);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = runner.RunAsync(cts.Token);
        await runner.Readings.ReadAsync(Ct);
        await cts.CancelAsync();
        await run;

        statuses.ShouldContain(s => s.Status == DeviceStatus.Disconnected && s.LastError != null);
    }

    [Fact]
    public async Task Cancellation_stops_cleanly_closes_transport_and_completes_readings()
    {
        var transport = new FakeTransport();
        var protocol = new ScriptedProtocol(YieldThenWaitAsync);
        var runner = new DeviceRunner("scale-1", () => transport, protocol, FastOptions);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = runner.RunAsync(cts.Token);
        await runner.Readings.ReadAsync(Ct);
        await cts.CancelAsync();

        await Should.NotThrowAsync(run);
        transport.CloseCount.ShouldBe(1);
        transport.DisposeCount.ShouldBe(1);
        await runner.Readings.Completion.WaitAsync(Ct);
        runner.Status.Status.ShouldBe(DeviceStatus.Disconnected);
        runner.Status.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task Cancellation_during_backoff_stops_cleanly()
    {
        var options = new DeviceRunnerOptions { InitialReconnectDelay = TimeSpan.FromMinutes(5), MaxReconnectDelay = TimeSpan.FromMinutes(5) };
        var runner = new DeviceRunner("scale-1", () => new FakeTransport { OpenException = new IOException("x") }, new ScriptedProtocol(EndAsync), options);
        var waiting = new TaskCompletionSource();
        runner.StatusChanged += (_, s) =>
        {
            if (s.RetryIn is not null)
            {
                waiting.TrySetResult();
            }
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = runner.RunAsync(cts.Token);
        await waiting.Task.WaitAsync(Ct);
        await cts.CancelAsync();

        await Should.NotThrowAsync(run.WaitAsync(TimeSpan.FromSeconds(5), Ct));
    }

    [Fact]
    public async Task Runner_can_only_be_started_once()
    {
        var runner = new DeviceRunner("scale-1", () => new FakeTransport(), new ScriptedProtocol(YieldThenWaitAsync), FastOptions);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = runner.RunAsync(cts.Token);

        await Should.ThrowAsync<InvalidOperationException>(() => runner.RunAsync(cts.Token));

        await cts.CancelAsync();
        await run;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(1000, 30)]
    public void Default_reconnect_delay_doubles_from_one_second_up_to_thirty(int failures, int expectedSeconds)
    {
        new DeviceRunnerOptions().GetReconnectDelay(failures).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    private static FakeTransport Add(List<FakeTransport> list, FakeTransport transport)
    {
        list.Add(transport);
        return transport;
    }

    private static ConcurrentQueue<DeviceStatusInfo> Record(DeviceRunner runner)
    {
        var statuses = new ConcurrentQueue<DeviceStatusInfo>();
        runner.StatusChanged += (_, s) => statuses.Enqueue(s);
        return statuses;
    }

    private static WeightReading Reading() =>
        new(572m, "g", WeightState.Stable, null, null, "S S        572 g", DateTimeOffset.UtcNow);

    private static async IAsyncEnumerable<WeightReading> YieldThenWaitAsync(ITransport transport, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return Reading();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static async IAsyncEnumerable<WeightReading> YieldThenLoseAsync(ITransport transport, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return Reading();
        await Task.Yield();
        throw new DeviceConnectionLostException();
    }

    private static async IAsyncEnumerable<WeightReading> EndAsync(ITransport transport, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield break;
    }

    /// <summary>Runs the given sessions in order, one per opened transport; the last one repeats.</summary>
    private sealed class ScriptedProtocol(params Func<ITransport, CancellationToken, IAsyncEnumerable<WeightReading>>[] sessions) : IScaleProtocol
    {
        private int _next;

        public IAsyncEnumerable<WeightReading> ReadAsync(ITransport transport, CancellationToken cancellationToken) =>
            sessions[Math.Min(_next++, sessions.Length - 1)](transport, cancellationToken);
    }
}
