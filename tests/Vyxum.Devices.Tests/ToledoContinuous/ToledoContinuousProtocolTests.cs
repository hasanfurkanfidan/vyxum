using Shouldly;
using Vyxum.Devices.ToledoContinuous;
using Xunit;

namespace Vyxum.Devices.Tests.ToledoContinuous;

public class ToledoContinuousProtocolTests
{
    private static readonly ToledoContinuousOptions Fast = new() { InactivityTimeout = TimeSpan.FromMilliseconds(100) };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_continuous_frames_and_never_writes()
    {
        var transport = new FakeTransport();
        var stable = new ToledoFrames { DecimalCode = 4 };
        var protocol = new ToledoContinuousProtocol(Fast);

        var task = TakeAsync(protocol, transport, 2);
        transport.Push([.. stable.Build(), .. (stable with { Motion = true, Weight = "001300" }).Build()]);
        var readings = await task;

        readings[0].Value.ShouldBe(12.34m);
        readings[0].State.ShouldBe(WeightState.Stable);
        readings[0].Unit.ShouldBe("kg");
        readings[0].IsNet.ShouldBe(false);
        readings[0].Tare.ShouldBe(0.00m);
        readings[0].Raw.ShouldBe(stable.Payload);
        readings[1].Value.ShouldBe(13.00m);
        readings[1].State.ShouldBe(WeightState.Motion);
        transport.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Frames_split_across_reads_are_joined()
    {
        var transport = new FakeTransport();
        var bytes = new ToledoFrames { DecimalCode = 5, Weight = "001250" }.Build(checksum: true);

        var task = TakeAsync(new ToledoContinuousProtocol(Fast with { ChecksumEnabled = true }), transport, 1);
        transport.Push(0x7F, 0x41);
        transport.Push(bytes[..3]);
        transport.Push(bytes[3..10]);
        transport.Push(bytes[10..]);
        var readings = await task;

        readings[0].Value.ShouldBe(1.250m);
    }

    [Fact]
    public async Task Discards_stale_input_on_start()
    {
        var transport = new FakeTransport();
        transport.Push(new ToledoFrames { Weight = "009999" }.Build());

        var task = TakeAsync(new ToledoContinuousProtocol(Fast), transport, 1);
        transport.Push(new ToledoFrames { Weight = "000001" }.Build());
        var readings = await task;

        transport.DiscardInputCount.ShouldBeGreaterThanOrEqualTo(1);
        readings[0].Value.ShouldBe(1m);
    }

    [Fact]
    public async Task Frames_with_wrong_checksum_are_skipped()
    {
        var transport = new FakeTransport();
        var bad = new ToledoFrames { Weight = "000111" }.Build(checksum: true);
        bad[^1] ^= 0x01;

        var task = TakeAsync(new ToledoContinuousProtocol(Fast with { ChecksumEnabled = true }), transport, 1);
        transport.Push([.. bad, .. new ToledoFrames { Weight = "000222" }.Build(checksum: true)]);
        var readings = await task;

        readings[0].Value.ShouldBe(222m);
    }

    [Fact]
    public async Task Silence_throws_connection_lost_after_the_inactivity_timeout()
    {
        var transport = new FakeTransport();

        var ex = await Should.ThrowAsync<DeviceConnectionLostException>(() => TakeAsync(new ToledoContinuousProtocol(Fast), transport, 1));

        ex.Message.ShouldContain("No valid frame");
    }

    [Fact]
    public async Task Valid_frames_keep_the_link_alive()
    {
        var transport = new FakeTransport();
        var protocol = new ToledoContinuousProtocol(Fast);
        using var feeding = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var feeder = Task.Run(
            async () =>
            {
                while (!feeding.IsCancellationRequested)
                {
                    transport.Push(new ToledoFrames().Build());
                    await Task.Delay(20, CancellationToken.None);
                }
            },
            CancellationToken.None);

        // 15 frames at 20 ms take about three inactivity timeouts.
        var readings = await TakeAsync(protocol, transport, 15);
        await feeding.CancelAsync();
        await feeder;

        readings.Count.ShouldBe(15);
    }

    [Fact]
    public async Task Too_many_invalid_frames_throw_connection_lost()
    {
        var transport = new FakeTransport();
        var options = Fast with { InactivityTimeout = TimeSpan.FromSeconds(5), MaxConsecutiveInvalidFrames = 5 };

        var task = TakeAsync(new ToledoContinuousProtocol(options), transport, 1);
        for (var i = 0; i < 5; i++)
        {
            transport.Push("\u0002garbage\r");
        }

        var ex = await Should.ThrowAsync<DeviceConnectionLostException>(() => task);
        ex.Message.ShouldContain("invalid frames");
    }

    [Fact]
    public async Task Invalid_frames_with_valid_ones_between_are_tolerated()
    {
        var transport = new FakeTransport();
        var options = Fast with { MaxConsecutiveInvalidFrames = 2 };

        var task = TakeAsync(new ToledoContinuousProtocol(options), transport, 3);
        for (var i = 0; i < 3; i++)
        {
            transport.Push("\u0002garbage\r");
            transport.Push(new ToledoFrames().Build());
        }

        (await task).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Overload_frame_yields_a_reading_without_value()
    {
        var transport = new FakeTransport();

        var task = TakeAsync(new ToledoContinuousProtocol(Fast), transport, 1);
        transport.Push(new ToledoFrames { OutOfRange = true }.Build());
        var readings = await task;

        readings[0].State.ShouldBe(WeightState.Overload);
        readings[0].Value.ShouldBeNull();
    }

    [Fact]
    public async Task Remote_close_throws_connection_lost()
    {
        var transport = new FakeTransport();
        transport.CloseRemote();

        var ex = await Should.ThrowAsync<DeviceConnectionLostException>(() => TakeAsync(new ToledoContinuousProtocol(Fast), transport, 1));
        ex.Message.ShouldContain("closed");
    }

    [Fact]
    public void Default_options()
    {
        var options = new ToledoContinuousOptions();

        options.ChecksumEnabled.ShouldBeFalse();
        options.InactivityTimeout.ShouldBe(TimeSpan.FromSeconds(3));
        options.MaxConsecutiveInvalidFrames.ShouldBe(20);
        Should.Throw<ArgumentOutOfRangeException>(() => new ToledoContinuousOptions { InactivityTimeout = TimeSpan.Zero });
        Should.Throw<ArgumentOutOfRangeException>(() => new ToledoContinuousOptions { MaxConsecutiveInvalidFrames = 0 });
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
