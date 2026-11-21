using System.Diagnostics;
using Shouldly;
using Vyxum.Devices.MtSics;
using Xunit;

namespace Vyxum.Devices.Tests.MtSics;

public class MtSicsProtocolTests
{
    private static readonly MtSicsProtocolOptions Fast = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ResponseTimeout = TimeSpan.FromMilliseconds(30),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_command_answered_with_ES_is_resent_and_succeeds()
    {
        var transport = Scale(n => n == 1 ? "ES" : "S S        572 g");

        var readings = await TakeAsync(new MtSicsProtocol(Fast), transport, 1);

        readings[0].Value.ShouldBe(572m);
        readings[0].Unit.ShouldBe("g");
        readings[0].State.ShouldBe(WeightState.Stable);
        readings[0].Raw.ShouldBe("S S        572 g");
        transport.Writes.ShouldBe(["SI\r\n", "SI\r\n"]);
    }

    [Fact]
    public async Task Discards_stale_input_on_start()
    {
        var transport = Scale(_ => "S S        572 g");
        transport.Push("S S        999 g\r\n");

        var readings = await TakeAsync(new MtSicsProtocol(Fast), transport, 1);

        transport.DiscardInputCount.ShouldBeGreaterThanOrEqualTo(1);
        readings[0].Value.ShouldBe(572m);
    }

    [Fact]
    public async Task Consecutive_timeouts_throw_connection_lost()
    {
        var transport = Scale(_ => null);

        await Should.ThrowAsync<DeviceConnectionLostException>(() => TakeAsync(new MtSicsProtocol(Fast), transport, 1));

        transport.Writes.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Timeout_count_resets_after_an_answer()
    {
        var transport = Scale(n => n % 3 == 0 ? "S S        572 g" : null);

        var readings = await TakeAsync(new MtSicsProtocol(Fast), transport, 3);

        readings.Count.ShouldBe(3);
        transport.Writes.Count.ShouldBe(9);
    }

    [Fact]
    public async Task Remote_close_throws_connection_lost()
    {
        var transport = new FakeTransport();
        transport.CloseRemote();

        await Should.ThrowAsync<DeviceConnectionLostException>(() => TakeAsync(new MtSicsProtocol(Fast), transport, 1));
    }

    [Fact]
    public async Task Busy_answer_yields_nothing_for_that_cycle()
    {
        var transport = Scale(n => n <= 3 ? "S I" : "S S      1.250 kg");

        var readings = await TakeAsync(new MtSicsProtocol(Fast), transport, 1);

        transport.Writes.Count.ShouldBe(4);
        readings[0].Value.ShouldBe(1.250m);
        readings[0].Value!.Value.Scale.ShouldBe((byte)3);
    }

    [Theory]
    [InlineData("S +", WeightState.Overload)]
    [InlineData("S -", WeightState.Underload)]
    public async Task Overload_and_underload_yield_a_reading_without_value(string answer, WeightState expected)
    {
        var transport = Scale(_ => answer);

        var readings = await TakeAsync(new MtSicsProtocol(Fast), transport, 1);

        readings[0].State.ShouldBe(expected);
        readings[0].Value.ShouldBeNull();
        readings[0].Unit.ShouldBeNull();
    }

    [Fact]
    public async Task Dynamic_negative_weight_is_motion()
    {
        var transport = Scale(_ => "S D     -12.34 g");

        var readings = await TakeAsync(new MtSicsProtocol(Fast), transport, 1);

        readings[0].State.ShouldBe(WeightState.Motion);
        readings[0].Value.ShouldBe(-12.34m);
    }

    [Theory]
    [InlineData("ES")]
    [InlineData("ET")]
    public async Task Exhausted_retries_skip_the_cycle_and_wait_for_the_poll_interval(string error)
    {
        var options = Fast with { PollInterval = TimeSpan.FromMilliseconds(60) };
        var writeTimes = new List<TimeSpan>();
        var clock = Stopwatch.StartNew();
        var transport = Scale(n =>
        {
            writeTimes.Add(clock.Elapsed);
            return n <= 3 ? error : "S S        572 g";
        });

        await TakeAsync(new MtSicsProtocol(options), transport, 1);

        transport.Writes.Count.ShouldBe(4);
        (writeTimes[3] - writeTimes[2]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public async Task Logical_error_is_not_retried_at_once()
    {
        var transport = Scale(n => n == 1 ? "EL" : "S S        572 g");

        await TakeAsync(new MtSicsProtocol(Fast), transport, 1);

        transport.Writes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Unknown_answer_skips_the_cycle()
    {
        var transport = Scale(n => n == 1 ? "garbage" : "S S        572 g");

        var readings = await TakeAsync(new MtSicsProtocol(Fast), transport, 1);

        readings[0].Value.ShouldBe(572m);
        transport.Writes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task S_command_is_sent_when_configured()
    {
        var transport = Scale(_ => "S S        572 g");

        await TakeAsync(new MtSicsProtocol(Fast with { Command = "S" }), transport, 2);

        transport.Writes.ShouldAllBe(w => w == "S\r\n");
    }

    [Fact]
    public async Task Sir_is_sent_once_and_the_stream_is_read()
    {
        var transport = new FakeTransport
        {
            Responder = _ => ["S D        570 g\r\nS D        571 g\r\n", "S S        572 g\r\n"],
        };

        var readings = await TakeAsync(new MtSicsProtocol(Fast with { Command = "SIR" }), transport, 3);

        transport.Writes.ShouldBe(["SIR\r\n"]);
        readings.Select(r => r.Value).ShouldBe([570m, 571m, 572m]);
        readings.Select(r => r.State).ShouldBe([WeightState.Motion, WeightState.Motion, WeightState.Stable]);
    }

    [Fact]
    public async Task Sir_answered_with_ES_is_resent()
    {
        var calls = 0;
        var transport = new FakeTransport
        {
            Responder = _ => ++calls == 1 ? ["ES\r\n"] : ["S S        572 g\r\n"],
        };

        var readings = await TakeAsync(new MtSicsProtocol(Fast with { Command = "SIR" }), transport, 1);

        transport.Writes.ShouldBe(["SIR\r\n", "SIR\r\n"]);
        readings[0].Value.ShouldBe(572m);
    }

    [Fact]
    public async Task Silent_sir_stream_throws_connection_lost()
    {
        var transport = new FakeTransport();

        await Should.ThrowAsync<DeviceConnectionLostException>(
            () => TakeAsync(new MtSicsProtocol(Fast with { Command = "SIR" }), transport, 1));
    }

    [Theory]
    [InlineData("S")]
    [InlineData("SI")]
    [InlineData("SIR")]
    public void Options_accept_read_commands(string command)
    {
        new MtSicsProtocolOptions { Command = command }.Command.ShouldBe(command);
    }

    [Theory]
    [InlineData("T")]
    [InlineData("Z")]
    [InlineData("TA")]
    [InlineData("TA 100 g")]
    [InlineData("@")]
    [InlineData("TI")]
    [InlineData("ZI")]
    [InlineData("TAC")]
    [InlineData("I4")]
    [InlineData("M01 1")]
    [InlineData("si")]
    [InlineData(" SI")]
    [InlineData("SI\r\nT")]
    [InlineData("")]
    public void Options_reject_any_other_command(string command)
    {
        Should.Throw<ArgumentException>(() => new MtSicsProtocolOptions { Command = command });
    }

    [Fact]
    public void Default_options_match_the_plan()
    {
        var options = new MtSicsProtocolOptions();

        options.Command.ShouldBe("SI");
        options.PollInterval.ShouldBe(TimeSpan.FromMilliseconds(200));
        options.ResponseTimeout.ShouldBe(TimeSpan.FromMilliseconds(1000));
        options.RetriesOnError.ShouldBe(2);
        options.MaxConsecutiveTimeouts.ShouldBe(3);
        options.LineTerminator.ShouldBe(LineTerminator.CrLf);
    }

    /// <summary>A fake scale that answers the n-th command (1-based) with the given line, or stays silent on null.</summary>
    private static FakeTransport Scale(Func<int, string?> answer)
    {
        var count = 0;
        return new FakeTransport
        {
            Responder = _ => answer(++count) is { } line ? [line + "\r\n"] : null,
        };
    }

    private static async Task<List<WeightReading>> TakeAsync(IScaleProtocol protocol, ITransport transport, int count)
    {
        var readings = new List<WeightReading>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await foreach (var reading in protocol.ReadAsync(transport, cts.Token))
        {
            readings.Add(reading);
            if (readings.Count == count)
            {
                break;
            }
        }

        return readings;
    }
}
