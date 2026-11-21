using Shouldly;
using Xunit;

namespace Vyxum.Devices.Tests;

public class FrameReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Line_split_across_reads_is_joined()
    {
        var transport = new FakeTransport();
        transport.Push("S S    ");
        transport.Push("    572");
        transport.Push(" g\r");
        transport.Push("\n");
        var reader = new FrameReader(transport);

        (await reader.ReadLineAsync(Ct)).ShouldBe("S S        572 g");
    }

    [Fact]
    public async Task Several_lines_in_one_read_are_split()
    {
        var transport = new FakeTransport();
        transport.Push("ES\r\nS S        573 g\r\nS S        572 g\r\n");
        var reader = new FrameReader(transport);

        (await reader.ReadLineAsync(Ct)).ShouldBe("ES");
        (await reader.ReadLineAsync(Ct)).ShouldBe("S S        573 g");
        (await reader.ReadLineAsync(Ct)).ShouldBe("S S        572 g");
    }

    [Fact]
    public async Task Byte_by_byte_input_gives_the_same_lines()
    {
        var transport = new FakeTransport();
        foreach (var b in "A\r\nBC\r\n"u8.ToArray())
        {
            transport.Push(b);
        }

        var reader = new FrameReader(transport);

        (await reader.ReadLineAsync(Ct)).ShouldBe("A");
        (await reader.ReadLineAsync(Ct)).ShouldBe("BC");
    }

    [Fact]
    public async Task Crlf_mode_keeps_a_lone_cr_inside_the_line_and_skips_empty_lines()
    {
        var transport = new FakeTransport();
        transport.Push("\r\nA\rB\r\n");
        var reader = new FrameReader(transport);

        (await reader.ReadLineAsync(Ct)).ShouldBe("A\rB");
    }

    [Theory]
    [InlineData(LineTerminator.Lf, "A\nB\n")]
    [InlineData(LineTerminator.Cr, "A\rB\r")]
    [InlineData(LineTerminator.Any, "A\r\nB\n")]
    [InlineData(LineTerminator.Any, "A\rB\r")]
    [InlineData(LineTerminator.Any, "A\n\r\nB\r")]
    public async Task Terminator_variants_split_lines(LineTerminator terminator, string input)
    {
        var transport = new FakeTransport();
        transport.Push(input);
        var reader = new FrameReader(transport, new FrameReaderOptions { Terminator = terminator });

        (await reader.ReadLineAsync(Ct)).ShouldBe("A");
        (await reader.ReadLineAsync(Ct)).ShouldBe("B");
    }

    [Fact]
    public async Task Overlong_line_is_discarded_and_reader_resyncs_on_next_terminator()
    {
        var transport = new FakeTransport();
        transport.Push(new string('x', 20) + "\r\nOK\r\n");
        var reader = new FrameReader(transport, new FrameReaderOptions { MaxFrameLength = 10 });

        (await reader.ReadLineAsync(Ct)).ShouldBe("OK");
        reader.OverflowCount.ShouldBe(1);
    }

    [Fact]
    public async Task Line_of_exactly_max_length_is_accepted()
    {
        var transport = new FakeTransport();
        transport.Push("0123456789\r\n");
        var reader = new FrameReader(transport, new FrameReaderOptions { MaxFrameLength = 10 });

        (await reader.ReadLineAsync(Ct)).ShouldBe("0123456789");
        reader.OverflowCount.ShouldBe(0);
    }

    [Fact]
    public async Task Overflow_resync_works_when_terminator_is_split_across_reads()
    {
        var transport = new FakeTransport();
        transport.Push(new string('x', 30) + "\r");
        transport.Push("\nOK\r\n");
        var reader = new FrameReader(transport, new FrameReaderOptions { MaxFrameLength = 10 });

        (await reader.ReadLineAsync(Ct)).ShouldBe("OK");
    }

    [Fact]
    public async Task Stx_mode_ignores_bytes_before_stx_and_strips_framing()
    {
        var transport = new FakeTransport();
        transport.Push("noise\u0002ABC\r\u0002DEF");
        transport.Push("\r");
        var reader = new FrameReader(transport, new FrameReaderOptions { Mode = FrameMode.StxCr });

        (await reader.ReadLineAsync(Ct)).ShouldBe("ABC");
        (await reader.ReadLineAsync(Ct)).ShouldBe("DEF");
    }

    [Fact]
    public async Task Stx_mode_returns_trailing_checksum_bytes()
    {
        var transport = new FakeTransport();
        transport.Push("\u0002AB\rZ\u0002CD\r");
        transport.Push("Y");
        var reader = new FrameReader(transport, new FrameReaderOptions { Mode = FrameMode.StxCr, TrailingByteCount = 1 });

        var first = await reader.ReadFrameAsync(Ct);
        var second = await reader.ReadFrameAsync(Ct);

        first.ShouldNotBeNull().Text.ShouldBe("AB");
        first.Trailer.ShouldBe("Z"u8.ToArray());
        second.ShouldNotBeNull().Text.ShouldBe("CD");
        second.Trailer.ShouldBe("Y"u8.ToArray());
    }

    [Fact]
    public async Task Stx_mode_restarts_on_unexpected_stx()
    {
        var transport = new FakeTransport();
        transport.Push("\u0002broken\u0002GOOD\r");
        var reader = new FrameReader(transport, new FrameReaderOptions { Mode = FrameMode.StxCr });

        (await reader.ReadLineAsync(Ct)).ShouldBe("GOOD");
    }

    [Fact]
    public async Task Stx_mode_overflow_waits_for_next_stx()
    {
        var transport = new FakeTransport();
        transport.Push("\u0002" + new string('x', 20) + "\rjunk\r\u0002OK\r");
        var reader = new FrameReader(transport, new FrameReaderOptions { Mode = FrameMode.StxCr, MaxFrameLength = 10 });

        (await reader.ReadLineAsync(Ct)).ShouldBe("OK");
        reader.OverflowCount.ShouldBe(1);
    }

    [Fact]
    public async Task Remote_close_returns_null()
    {
        var transport = new FakeTransport();
        transport.Push("partial");
        transport.CloseRemote();
        var reader = new FrameReader(transport);

        (await reader.ReadLineAsync(Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Timeout_cancels_the_read_and_keeps_the_partial_line()
    {
        var transport = new FakeTransport();
        transport.Push("S S   ");
        var reader = new FrameReader(transport);

        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct))
        {
            timeout.CancelAfter(TimeSpan.FromMilliseconds(20));
            await Should.ThrowAsync<OperationCanceledException>(async () => await reader.ReadLineAsync(timeout.Token));
        }

        transport.Push("     572 g\r\n");
        (await reader.ReadLineAsync(Ct)).ShouldBe("S S        572 g");
    }

    [Fact]
    public async Task Reset_drops_the_partial_line()
    {
        var transport = new FakeTransport();
        transport.Push("stale");
        var reader = new FrameReader(transport);
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct))
        {
            timeout.CancelAfter(TimeSpan.FromMilliseconds(20));
            await Should.ThrowAsync<OperationCanceledException>(async () => await reader.ReadLineAsync(timeout.Token));
        }

        reader.Reset();
        transport.Push("fresh\r\n");

        (await reader.ReadLineAsync(Ct)).ShouldBe("fresh");
    }

    [Fact]
    public void Options_reject_invalid_values()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FrameReaderOptions { MaxFrameLength = 0 });
        Should.Throw<ArgumentOutOfRangeException>(() => new FrameReaderOptions { TrailingByteCount = -1 });
    }
}
