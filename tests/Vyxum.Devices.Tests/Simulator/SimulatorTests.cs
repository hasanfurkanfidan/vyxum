using System.Collections.Concurrent;
using System.Text;
using Shouldly;
using Vyxum.Devices.MtSics;
using Vyxum.Devices.Simulator;
using Vyxum.Devices.ToledoContinuous;
using Vyxum.Devices.Transports;
using Xunit;

namespace Vyxum.Devices.Tests.Simulator;

public class SimulatorTests
{
    private static readonly MtSicsProtocolOptions FastMtSics = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(1),
        ResponseTimeout = TimeSpan.FromMilliseconds(500),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string RecordingPath =>
        Path.Combine(AppContext.BaseDirectory, "Recordings", "ind231-2026-11-21.txt");

    private static async Task<List<WeightReading>> ReadAsync(IScaleProtocol protocol, ITransport transport, int count)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        var readings = new List<WeightReading>();
        await transport.OpenAsync(cts.Token);
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

    private static TcpTransport Tcp(SimulatorTcpServer server) =>
        new(new TcpTransportOptions { Host = "127.0.0.1", Port = server.Port });

    [Fact]
    public async Task Ind231_recording_through_real_tcp_and_mtsics_matches_direct_parsing()
    {
        var lines = LineReplaySimulator.ReadRecording(RecordingPath);
        var expected = lines.Select(MtSicsParser.Parse).Where(r => r.Kind == MtSicsResponseKind.Weight).ToList();
        expected.Count.ShouldBe(13);

        await using var server = new SimulatorTcpServer(LineReplaySimulator.FromFile(RecordingPath, loop: false)).Start();
        await using var transport = Tcp(server);
        var readings = await ReadAsync(new MtSicsProtocol(FastMtSics), transport, expected.Count);

        readings.Select(r => (r.Value, r.Unit, r.State)).ShouldBe(
            expected.Select(e => (e.Value, e.Unit, e.IsStable ? WeightState.Stable : WeightState.Motion)));
        readings[0].Value.ShouldBe(572m);
        readings[0].Unit.ShouldBe("g");
        readings[0].State.ShouldBe(WeightState.Stable);
        readings[^1].Value.ShouldBe(573m);
    }

    [Fact]
    public async Task Mtsics_simulator_through_real_tcp_gives_572_g_stable()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m, "g"), "C139673552");
        await using var server = new SimulatorTcpServer(device).Start();
        await using var transport = Tcp(server);

        var readings = await ReadAsync(new MtSicsProtocol(FastMtSics), transport, 3);

        readings.ShouldAllBe(r => r.Value == 572m && r.Unit == "g" && r.State == WeightState.Stable);
        readings[0].Raw.ShouldBe("S S        572 g");
        server.AcceptedConnections.ShouldBe(1);
        device.ForbiddenCommands.ShouldBeEmpty();
    }

    [Fact]
    public async Task First_command_ES_is_overcome_by_the_driver()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m)) { FirstCommandReturnsSyntaxError = true };
        await using var transport = new SimulatedTransport(device);

        var readings = await ReadAsync(new MtSicsProtocol(FastMtSics), transport, 2);

        readings.ShouldAllBe(r => r.Value == 572m && r.State == WeightState.Stable);
        device.ReceivedCommands.Take(2).ShouldBe(["SI", "SI"]);
        device.ForbiddenCommands.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1.250, "kg", WeightState.Motion, "S D      1.250 kg")]
    [InlineData(-12.34, "g", WeightState.Stable, "S S     -12.34 g")]
    [InlineData(572, "g", WeightState.Stable, "S S        572 g")]
    public async Task Mtsics_weight_states_and_wire_format(double value, string unit, WeightState state, string line)
    {
        var weight = new SimulatedWeight(value switch { 1.250 => 1.250m, -12.34 => -12.34m, _ => 572m }, unit, state);
        MtSicsScaleSimulator.FormatWeightLine(weight).ShouldBe(line);

        var device = new MtSicsScaleSimulator(new FixedWeightSource(weight));
        await using var transport = new SimulatedTransport(device);
        var reading = (await ReadAsync(new MtSicsProtocol(FastMtSics), transport, 1)).Single();

        reading.Value.ShouldBe(weight.Value);
        reading.Unit.ShouldBe(unit);
        reading.State.ShouldBe(state);
        reading.Raw.ShouldBe(line);
    }

    [Theory]
    [InlineData(WeightState.Overload, "S +")]
    [InlineData(WeightState.Underload, "S -")]
    public async Task Mtsics_overload_and_underload(WeightState state, string line)
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(0m, "g", state));
        await using var transport = new SimulatedTransport(device);

        var reading = (await ReadAsync(new MtSicsProtocol(FastMtSics), transport, 1)).Single();

        reading.State.ShouldBe(state);
        reading.Value.ShouldBeNull();
        reading.Raw.ShouldBe(line);
    }

    [Fact]
    public async Task Scripted_sequence_goes_from_motion_to_stable_and_repeats_the_last_step()
    {
        var source = new ScriptedWeightSource(
        [
            new SimulatedWeight(100m, "g", WeightState.Motion),
            new SimulatedWeight(250m, "g", WeightState.Motion),
            new SimulatedWeight(300m, "g"),
        ]);
        var device = new MtSicsScaleSimulator(source);
        await using var transport = new SimulatedTransport(device);

        var readings = await ReadAsync(new MtSicsProtocol(FastMtSics), transport, 4);

        readings.Select(r => (r.Value, r.State)).ShouldBe(
        [
            (100m, WeightState.Motion),
            (250m, WeightState.Motion),
            (300m, WeightState.Stable),
            (300m, WeightState.Stable),
        ]);
    }

    [Fact]
    public async Task Sir_stream_is_read_and_only_read_commands_are_sent()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m)) { StreamInterval = TimeSpan.FromMilliseconds(1) };
        await using var transport = new SimulatedTransport(device);

        var readings = await ReadAsync(new MtSicsProtocol(FastMtSics with { Command = "SIR" }), transport, 5);

        readings.ShouldAllBe(r => r.Value == 572m);
        device.ReceivedCommands.ShouldAllBe(c => c == "SIR");
        device.ForbiddenCommands.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("S")]
    [InlineData("SI")]
    public async Task Driver_never_sends_tare_zero_or_reset(string command)
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m)) { FirstCommandReturnsSyntaxError = true };
        await using var transport = new SimulatedTransport(device);

        await ReadAsync(new MtSicsProtocol(FastMtSics with { Command = command }), transport, 10);

        device.ReceivedCommands.ShouldNotBeEmpty();
        device.ReceivedCommands.ShouldAllBe(c => c == command);
        device.ForbiddenCommands.ShouldBeEmpty();
    }

    [Fact]
    public async Task Simulator_records_tare_immediate_zero_and_reset_when_they_are_sent()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m), "C1");
        await using var transport = new SimulatedTransport(device);
        await transport.OpenAsync(Ct);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("T\r\nZI\r\n@\r\nZ\r\nI4\r\nSI\r\n"), Ct);
        await ReadLinesAsync(transport, 6);

        device.ForbiddenCommands.ShouldBe(["T", "ZI", "@"]);
        device.ReceivedCommands.ShouldBe(["T", "ZI", "@", "Z", "I4", "SI"]);
    }

    [Fact]
    public async Task Z_zeroes_the_simulator_so_later_weights_are_relative_to_it()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(1.250m, "kg"));
        await using var transport = new SimulatedTransport(device);
        await transport.OpenAsync(Ct);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("SI\r\nZ\r\nSI\r\n"), Ct);
        var lines = await ReadLinesAsync(transport, 3);

        lines.ShouldBe(["S S      1.250 kg", "Z A", "S S      0.000 kg"]);
        device.ZeroOffset.ShouldBe(1.250m);
        device.ZeroCommandCount.ShouldBe(1);
        device.ForbiddenCommands.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(WeightState.Motion, "Z I")]
    [InlineData(WeightState.Overload, "Z +")]
    [InlineData(WeightState.Underload, "Z -")]
    public async Task Z_is_refused_while_the_weight_is_not_stable_or_out_of_range(WeightState state, string expected)
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m, "g", state));
        await using var transport = new SimulatedTransport(device);
        await transport.OpenAsync(Ct);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("Z\r\n"), Ct);
        var lines = await ReadLinesAsync(transport, 1);

        lines.ShouldBe([expected]);
        device.ZeroOffset.ShouldBe(0m);
    }

    [Fact]
    public async Task Protocol_zero_on_the_simulator_makes_the_next_reading_zero()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m));
        await using var transport = new SimulatedTransport(device);
        var protocol = new MtSicsProtocol(FastMtSics);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await transport.OpenAsync(cts.Token);
        await using var readings = protocol.ReadAsync(transport, cts.Token).GetAsyncEnumerator(cts.Token);

        (await readings.MoveNextAsync()).ShouldBeTrue();
        readings.Current.Value.ShouldBe(572m);
        var zero = protocol.ZeroAsync(cts.Token);
        decimal? value = null;
        while (!zero.IsCompleted || value != 0m)
        {
            (await readings.MoveNextAsync()).ShouldBeTrue();
            value = readings.Current.Value;
        }

        (await zero).ShouldBe(ZeroResult.Done);
        device.ZeroCommandCount.ShouldBe(1);
        device.ForbiddenCommands.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_command_is_answered_with_ES_and_I4_with_the_serial_number()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(1m), "C139673552");
        await using var transport = new SimulatedTransport(device);
        await transport.OpenAsync(Ct);

        await transport.WriteAsync(Encoding.ASCII.GetBytes("XYZ\r\nI4\r\n"), Ct);
        var lines = await ReadLinesAsync(transport, 2);

        lines.ShouldBe(["ES", "I4 A \"C139673552\""]);
        MtSicsParser.Parse(lines[1]).SerialNumber.ShouldBe("C139673552");
    }

    [Fact]
    public async Task Silent_device_makes_the_driver_give_up_with_connection_lost()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m)) { Silent = true };
        await using var transport = new SimulatedTransport(device);
        var options = FastMtSics with { ResponseTimeout = TimeSpan.FromMilliseconds(20), MaxConsecutiveTimeouts = 2 };

        await Should.ThrowAsync<DeviceConnectionLostException>(() => ReadAsync(new MtSicsProtocol(options), transport, 1));

        device.ReceivedCommands.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Busy_device_gives_no_readings_until_it_is_ready()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m)) { Busy = true };
        await using var transport = new SimulatedTransport(device);

        var reading = ReadAsync(new MtSicsProtocol(FastMtSics), transport, 1);
        while (device.ReceivedCommands.Count < 3)
        {
            await Task.Delay(1, Ct);
        }

        reading.IsCompleted.ShouldBeFalse();
        device.Busy = false;

        (await reading).Single().Value.ShouldBe(572m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Toledo_simulator_through_real_tcp_and_protocol(bool checksum)
    {
        var device = new ToledoContinuousSimulator(new FixedWeightSource(1.250m, "kg"), TimeSpan.FromMilliseconds(2), checksum);
        await using var server = new SimulatorTcpServer(device).Start();
        await using var transport = Tcp(server);

        var readings = await ReadAsync(new ToledoContinuousProtocol(new ToledoContinuousOptions { ChecksumEnabled = checksum }), transport, 3);

        readings.ShouldAllBe(r => r.Value == 1.250m && r.Unit == "kg" && r.State == WeightState.Stable);
    }

    [Fact]
    public async Task Toledo_frame_checksum_matches_the_parser_formula()
    {
        var frame = ToledoContinuousSimulator.BuildFrame(new SimulatedWeight(572m, "g"), checksum: true);

        frame.Length.ShouldBe(18);
        ToledoContinuousParser.ComputeChecksum(frame.AsSpan(0, 17)).ShouldBe(frame[17]);
        ToledoContinuousParser.Parse(frame, checksumEnabled: true).IsValid.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Toledo_simulator_states_negative_and_out_of_range()
    {
        var source = new ScriptedWeightSource(
        [
            new SimulatedWeight(1.250m, "kg", WeightState.Motion),
            new SimulatedWeight(-12.34m, "g"),
            new SimulatedWeight(0m, "kg", WeightState.Overload),
            new SimulatedWeight(0m, "kg", WeightState.Underload),
        ]);
        var device = new ToledoContinuousSimulator(source, TimeSpan.FromMilliseconds(2), checksum: true);
        await using var transport = new SimulatedTransport(device);

        var readings = await ReadAsync(new ToledoContinuousProtocol(new ToledoContinuousOptions { ChecksumEnabled = true }), transport, 4);

        readings.Select(r => (r.Value, r.Unit, r.State)).ShouldBe(
        [
            (1.250m, "kg", WeightState.Motion),
            (-12.34m, "g", WeightState.Stable),
            (null, "kg", WeightState.Overload),
            (null, "kg", WeightState.Underload),
        ]);
    }

    [Fact]
    public async Task Runner_reconnects_after_disconnect_and_readings_resume()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m));
        var transport = new SimulatedTransport(device);
        var runner = new DeviceRunner(
            "sim",
            () => transport,
            new MtSicsProtocol(FastMtSics),
            new DeviceRunnerOptions { InitialReconnectDelay = TimeSpan.FromMilliseconds(2), MaxReconnectDelay = TimeSpan.FromMilliseconds(8) });
        var statuses = new ConcurrentQueue<DeviceStatusInfo>();
        runner.StatusChanged += (_, s) => statuses.Enqueue(s);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var run = runner.RunAsync(cts.Token);
        await runner.Readings.ReadAsync(cts.Token);
        transport.Disconnect();
        while (!(HasReconnected(statuses)))
        {
            await runner.Readings.ReadAsync(cts.Token);
        }

        await runner.Readings.ReadAsync(cts.Token);
        await cts.CancelAsync();
        await run;

        transport.OpenCount.ShouldBe(2);
        statuses.ShouldContain(s => s.Status == DeviceStatus.Disconnected && s.LastError != null);
    }

    [Fact]
    public async Task Runner_retries_open_failures()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(572m));
        var transport = new SimulatedTransport(device);
        transport.FailNextOpens(2);
        var runner = new DeviceRunner(
            "sim",
            () => transport,
            new MtSicsProtocol(FastMtSics),
            new DeviceRunnerOptions { InitialReconnectDelay = TimeSpan.FromMilliseconds(2), MaxReconnectDelay = TimeSpan.FromMilliseconds(8) });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var run = runner.RunAsync(cts.Token);
        var reading = await runner.Readings.ReadAsync(cts.Token);
        await cts.CancelAsync();
        await run;

        reading.Value.ShouldBe(572m);
        transport.OpenCount.ShouldBe(1);
    }

    [Fact]
    public async Task Transport_disconnect_ends_reads_with_zero_and_makes_writes_fail()
    {
        await using var transport = new SimulatedTransport(new MtSicsScaleSimulator(new FixedWeightSource(1m)));
        await transport.OpenAsync(Ct);
        var pending = transport.ReadAsync(new byte[16], Ct).AsTask();

        transport.Disconnect();

        (await pending.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe(0);
        (await transport.ReadAsync(new byte[16], Ct)).ShouldBe(0);
        await Should.ThrowAsync<IOException>(async () => await transport.WriteAsync(new byte[] { 1 }, Ct));
    }

    [Fact]
    public async Task Transport_can_be_closed_and_opened_again_and_open_failures_are_counted_down()
    {
        await using var transport = new SimulatedTransport(new MtSicsScaleSimulator(new FixedWeightSource(1m)));
        transport.FailNextOpens(1);

        await Should.ThrowAsync<IOException>(() => transport.OpenAsync(Ct));
        await transport.OpenAsync(Ct);
        transport.IsOpen.ShouldBeTrue();
        await transport.CloseAsync();
        transport.IsOpen.ShouldBeFalse();
        await transport.OpenAsync(Ct);

        transport.OpenCount.ShouldBe(2);
    }

    [Fact]
    public async Task Silent_transport_drops_device_output_but_delivers_commands()
    {
        var device = new MtSicsScaleSimulator(new FixedWeightSource(1m));
        await using var transport = new SimulatedTransport(device);
        await transport.OpenAsync(Ct);
        transport.GoSilent();

        await transport.WriteAsync(Encoding.ASCII.GetBytes("SI\r\n"), Ct);
        while (device.ReceivedCommands.Count == 0)
        {
            await Task.Delay(1, Ct);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(100));
        await Should.ThrowAsync<OperationCanceledException>(async () => await transport.ReadAsync(new byte[16], timeout.Token));
    }

    [Fact]
    public void Recorded_weight_source_and_replay_skip_comments_and_non_weight_lines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyxum-rec-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "# comment\r\n\r\nS S        572 g\r\nES\r\nS D      1.250 kg\r\nS +\r\n");
        try
        {
            LineReplaySimulator.ReadRecording(path).ShouldBe(["S S        572 g", "ES", "S D      1.250 kg", "S +"]);
            var source = RecordedWeightSource.FromFile(path, loop: true);

            source.Next().ShouldBe(new SimulatedWeight(572m, "g"));
            source.Next().ShouldBe(new SimulatedWeight(1.250m, "kg", WeightState.Motion));
            source.Next().State.ShouldBe(WeightState.Overload);
            source.Next().Value.ShouldBe(572m);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Factory_builds_devices_from_options()
    {
        var mtsics = SimulatorFactory.CreateTransport(new SimulatorOptions { Weight = 1.250m, Unit = "kg" });
        (await ReadAsync(new MtSicsProtocol(FastMtSics), mtsics, 1)).Single().Value.ShouldBe(1.250m);

        var toledo = SimulatorFactory.CreateTransport(new SimulatorOptions
        {
            Kind = SimulatorKind.ToledoContinuous,
            Weight = 5m,
            Unit = "kg",
            Interval = TimeSpan.FromMilliseconds(2),
        });
        (await ReadAsync(new ToledoContinuousProtocol(), toledo, 1)).Single().Value.ShouldBe(5m);

        var replay = SimulatorFactory.CreateTransport(new SimulatorOptions { Kind = SimulatorKind.Replay, RecordingPath = RecordingPath });
        (await ReadAsync(new MtSicsProtocol(FastMtSics), replay, 3)).Select(r => r.Value).ShouldBe([572m, 572m, 573m]);

        Should.Throw<ArgumentException>(() => SimulatorFactory.CreateDevice(new SimulatorOptions { Kind = SimulatorKind.Replay }));
    }

    [Fact]
    public async Task Replay_in_interval_mode_sends_lines_without_requests()
    {
        var device = new LineReplaySimulator(["S S        572 g", "S S        573 g"], ReplayMode.Interval, TimeSpan.FromMilliseconds(2));
        await using var transport = new SimulatedTransport(device);

        var lines = await ReadLinesAsync(transport, 3, open: true);

        lines.ShouldBe(["S S        572 g", "S S        573 g", "S S        572 g"]);
    }

    private static bool HasReconnected(ConcurrentQueue<DeviceStatusInfo> statuses)
    {
        var list = statuses.ToArray();
        var lost = Array.FindIndex(list, s => s.Status == DeviceStatus.Disconnected && s.LastError != null);
        return lost >= 0 && list.Skip(lost).Any(s => s.Status == DeviceStatus.Connected);
    }

    private static async Task<List<string>> ReadLinesAsync(ITransport transport, int count, bool open = false)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        if (open)
        {
            await transport.OpenAsync(cts.Token);
        }

        var reader = new FrameReader(transport, new FrameReaderOptions());
        var lines = new List<string>();
        while (lines.Count < count)
        {
            lines.Add((await reader.ReadLineAsync(cts.Token))!);
        }

        return lines;
    }
}
