using Shouldly;
using Vyxum.Devices.GenericAscii;
using Xunit;

namespace Vyxum.Devices.Tests.GenericAscii;

public class GenericAsciiProtocolTests
{
    /// <summary>A print-on-request indicator: "P" asks for the weight, '?' marks motion.</summary>
    private static readonly GenericAsciiOptions Demand = new()
    {
        Pattern = @"^\s*(?<weight>-?\d+\.\d+)\s*(?<unit>[a-z]+)\s*(?<status>\??)\s*$",
        MotionPattern = @"\?",
        RequestCommand = "P",
        PollInterval = TimeSpan.FromMilliseconds(5),
        ResponseTimeout = TimeSpan.FromMilliseconds(40),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listens_to_continuous_header_style_output_without_writing()
    {
        var transport = new FakeTransport();
        var protocol = new GenericAsciiProtocol(GenericAsciiParserTests.HeaderStyle with { InactivityTimeout = TimeSpan.FromSeconds(5) });

        var task = TakeAsync(protocol, transport, 3);
        transport.Push("ST,GS,+0001.2");
        transport.Push("50kg\r\nnoise\r\nUS,GS,+0001.300kg\r\n");
        transport.Push("OL,GS,+9999.999kg\r\n");
        var readings = await task;

        readings.Select(r => r.Value).ShouldBe([1.250m, 1.300m, null]);
        readings.Select(r => r.State).ShouldBe([WeightState.Stable, WeightState.Motion, WeightState.Overload]);
        transport.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Listens_to_comma_style_output_with_lf_terminator()
    {
        var transport = new FakeTransport();
        var protocol = new GenericAsciiProtocol(GenericAsciiParserTests.CommaStyle with { Terminator = LineTerminator.Lf });

        var task = TakeAsync(protocol, transport, 2);
        transport.Push("?    12,300 kg\n     12,345 kg\n");
        var readings = await task;

        readings[0].State.ShouldBe(WeightState.Motion);
        readings[1].Value.ShouldBe(12.345m);
        readings[1].State.ShouldBe(WeightState.Stable);
    }

    [Fact]
    public async Task Discards_stale_input_on_start()
    {
        var transport = new FakeTransport();
        transport.Push("ST,GS,+0009.999kg\r\n");

        var task = TakeAsync(new GenericAsciiProtocol(GenericAsciiParserTests.HeaderStyle), transport, 1);
        transport.Push("ST,GS,+0001.000kg\r\n");
        var readings = await task;

        transport.DiscardInputCount.ShouldBeGreaterThanOrEqualTo(1);
        readings[0].Value.ShouldBe(1.000m);
    }

    [Fact]
    public async Task Silence_throws_connection_lost_after_the_inactivity_timeout()
    {
        var protocol = new GenericAsciiProtocol(GenericAsciiParserTests.HeaderStyle with { InactivityTimeout = TimeSpan.FromMilliseconds(80) });

        var ex = await Should.ThrowAsync<DeviceConnectionLostException>(() => TakeAsync(protocol, new FakeTransport(), 1));

        ex.Message.ShouldContain("nothing was received");
    }

    [Fact]
    public async Task Only_unmatched_lines_throw_connection_lost_with_a_hint()
    {
        var transport = new FakeTransport();
        var protocol = new GenericAsciiProtocol(GenericAsciiParserTests.HeaderStyle with { InactivityTimeout = TimeSpan.FromMilliseconds(80) });

        var task = TakeAsync(protocol, transport, 1);
        transport.Push("garbage\r\nmore garbage\r\n");

        var ex = await Should.ThrowAsync<DeviceConnectionLostException>(() => task);
        ex.Message.ShouldContain("none matched the pattern");
    }

    [Fact]
    public async Task Remote_close_throws_connection_lost()
    {
        var transport = new FakeTransport();
        transport.CloseRemote();

        await Should.ThrowAsync<DeviceConnectionLostException>(() => TakeAsync(new GenericAsciiProtocol(GenericAsciiParserTests.HeaderStyle), transport, 1));
    }

    [Fact]
    public async Task Demand_mode_sends_the_request_and_reads_the_answer()
    {
        var count = 0;
        var transport = new FakeTransport
        {
            Responder = line => line == "P" ? [++count == 1 ? "    1.250 kg ?\r\n" : "    1.300 kg\r\n"] : null,
        };

        var readings = await TakeAsync(new GenericAsciiProtocol(Demand), transport, 2);

        transport.Writes.ShouldBe(["P\r\n", "P\r\n"]);
        readings[0].Value.ShouldBe(1.250m);
        readings[0].State.ShouldBe(WeightState.Motion);
        readings[1].Value.ShouldBe(1.300m);
        readings[1].State.ShouldBe(WeightState.Stable);
        readings[1].Unit.ShouldBe("kg");
    }

    [Fact]
    public async Task Demand_mode_skips_echo_and_header_lines()
    {
        var transport = new FakeTransport { Responder = _ => ["P\r\nGROSS\r\n   -0.500 kg\r\n"] };

        var readings = await TakeAsync(new GenericAsciiProtocol(Demand), transport, 1);

        readings[0].Value.ShouldBe(-0.500m);
        transport.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Demand_mode_uses_the_configured_terminator()
    {
        var transport = new FakeTransport { Responder = _ => ["    2.000 kg\r\n"] };

        await TakeAsync(new GenericAsciiProtocol(Demand with { RequestCommand = "W", RequestTerminator = "\r" }), transport, 1);

        transport.Writes[0].ShouldBe("W\r");
    }

    [Fact]
    public async Task Demand_mode_throws_after_consecutive_timeouts()
    {
        var transport = new FakeTransport { Responder = _ => null };

        var ex = await Should.ThrowAsync<DeviceConnectionLostException>(() => TakeAsync(new GenericAsciiProtocol(Demand), transport, 1));

        transport.Writes.Count.ShouldBe(3);
        ex.Message.ShouldContain("3 requests");
    }

    [Fact]
    public async Task Demand_mode_timeout_count_resets_after_an_answer()
    {
        var count = 0;
        var transport = new FakeTransport { Responder = _ => ++count % 3 == 0 ? ["    1.000 kg\r\n"] : null };

        var readings = await TakeAsync(new GenericAsciiProtocol(Demand), transport, 2);

        readings.Count.ShouldBe(2);
        transport.Writes.Count.ShouldBe(6);
    }

    [Theory]
    [InlineData("T")]
    [InlineData("Z")]
    [InlineData("R")]
    [InlineData("p")]
    [InlineData("SI ")]
    [InlineData("CAL")]
    public void Request_command_that_is_not_a_known_read_command_is_rejected(string command)
    {
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(Demand with { RequestCommand = command }))
            .ParamName.ShouldBe(nameof(GenericAsciiOptions.RequestCommand));
    }

    [Fact]
    public void Every_allowed_read_command_and_listen_only_mode_are_accepted()
    {
        foreach (var command in GenericAsciiOptions.AllowedRequestCommands)
        {
            Should.NotThrow(() => new GenericAsciiProtocol(Demand with { RequestCommand = command }));
        }

        Should.NotThrow(() => new GenericAsciiProtocol(Demand with { RequestCommand = null }));
        Should.NotThrow(() => new GenericAsciiProtocol(Demand with { RequestCommand = "" }));
    }

    [Theory]
    [InlineData("P\r\nT")]
    [InlineData("T\n")]
    public void Request_command_with_line_breaks_is_rejected(string command)
    {
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(Demand with { RequestCommand = command }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\nT\r\n")]
    [InlineData(";")]
    public void Request_terminator_other_than_cr_and_lf_is_rejected(string terminator)
    {
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(Demand with { RequestTerminator = terminator }));
    }

    [Fact]
    public void Other_invalid_settings_are_rejected()
    {
        var valid = GenericAsciiParserTests.HeaderStyle;

        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(valid with { DecimalSeparator = ";" }));
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(valid with { ThousandsSeparator = "." }));
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(valid with { PollInterval = TimeSpan.Zero }));
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(valid with { ResponseTimeout = TimeSpan.Zero }));
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(valid with { InactivityTimeout = TimeSpan.Zero }));
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(valid with { MaxConsecutiveTimeouts = 0 }));
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(valid with { Terminator = (LineTerminator)99 }));
    }

    [Fact]
    public void Default_options()
    {
        var options = new GenericAsciiOptions();

        options.DecimalSeparator.ShouldBe(".");
        options.ThousandsSeparator.ShouldBeNull();
        options.Terminator.ShouldBe(LineTerminator.Any);
        options.RequestCommand.ShouldBeNull();
        options.IsDemandMode.ShouldBeFalse();
        options.RequestTerminator.ShouldBe("\r\n");
        options.PollInterval.ShouldBe(TimeSpan.FromMilliseconds(200));
        options.ResponseTimeout.ShouldBe(TimeSpan.FromMilliseconds(1000));
        options.MaxConsecutiveTimeouts.ShouldBe(3);
        options.InactivityTimeout.ShouldBe(TimeSpan.FromSeconds(3));
        Should.Throw<ArgumentException>(() => new GenericAsciiProtocol(options));
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
