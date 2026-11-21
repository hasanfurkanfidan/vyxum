using System.Threading.Channels;
using Shouldly;
using Vyxum.Devices.MtSics;
using Xunit;

namespace Vyxum.Devices.Tests.MtSics;

public class MtSicsZeroTests
{
    private static readonly MtSicsProtocolOptions Fast = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ResponseTimeout = TimeSpan.FromMilliseconds(200),
        ZeroTimeout = TimeSpan.FromMilliseconds(300),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Z A", ZeroOutcome.Done)]
    [InlineData("Z I", ZeroOutcome.Rejected)]
    [InlineData("Z +", ZeroOutcome.Rejected)]
    [InlineData("Z -", ZeroOutcome.Rejected)]
    [InlineData("EL", ZeroOutcome.Rejected)]
    public async Task Answer_to_Z_is_mapped(string answer, ZeroOutcome expected)
    {
        var transport = Scale(_ => [answer]);
        await using var run = await Running.StartAsync(new MtSicsProtocol(Fast), transport);

        var result = await run.Protocol.ZeroAsync(Ct);

        result.Outcome.ShouldBe(expected);
        if (expected == ZeroOutcome.Rejected)
        {
            result.Detail.ShouldNotBeNullOrWhiteSpace();
            result.Detail.ShouldContain(answer);
        }

        transport.Writes.Count(w => w == "Z\r\n").ShouldBe(1);
    }

    [Theory]
    [InlineData("ES")]
    [InlineData("ET")]
    public async Task Z_answered_with_an_error_is_resent(string error)
    {
        var zeros = 0;
        var transport = Scale(_ => ++zeros == 1 ? [error] : ["Z A"]);
        await using var run = await Running.StartAsync(new MtSicsProtocol(Fast), transport);

        var result = await run.Protocol.ZeroAsync(Ct);

        result.ShouldBe(ZeroResult.Done);
        transport.Writes.Count(w => w == "Z\r\n").ShouldBe(2);
    }

    [Fact]
    public async Task Z_answered_with_ES_every_time_is_rejected_after_the_retries()
    {
        var transport = Scale(_ => ["ES"]);
        await using var run = await Running.StartAsync(new MtSicsProtocol(Fast), transport);

        var result = await run.Protocol.ZeroAsync(Ct);

        result.Outcome.ShouldBe(ZeroOutcome.Rejected);
        transport.Writes.Count(w => w == "Z\r\n").ShouldBe(Fast.RetriesOnError + 1);
    }

    [Fact]
    public async Task Unanswered_Z_is_unavailable_and_not_resent()
    {
        var transport = Scale(_ => null);
        await using var run = await Running.StartAsync(new MtSicsProtocol(Fast), transport);

        var result = await run.Protocol.ZeroAsync(Ct);

        result.Outcome.ShouldBe(ZeroOutcome.Unavailable);
        transport.Writes.Count(w => w == "Z\r\n").ShouldBe(1);

        // Polling goes on after the timeout.
        await run.WaitForReadingsAsync(run.Count + 2);
    }

    [Fact]
    public async Task Z_is_sent_between_two_polls_and_never_interleaved()
    {
        var transport = Scale(_ => ["Z A"]);
        await using var run = await Running.StartAsync(new MtSicsProtocol(Fast), transport);

        await run.Protocol.ZeroAsync(Ct);
        await run.WaitForReadingsAsync(run.Count + 1);

        // Every command is answered before the next one is written: the answers are taken in order by the fake.
        var writes = transport.Writes;
        var index = writes.ToList().IndexOf("Z\r\n");
        index.ShouldBeGreaterThan(0);
        writes[index - 1].ShouldBe("SI\r\n");
        writes[index + 1].ShouldBe("SI\r\n");
        transport.MaxOutstanding.ShouldBe(1);
    }

    [Fact]
    public async Task Each_call_sends_Z_exactly_once()
    {
        var transport = Scale(_ => ["Z A"]);
        await using var run = await Running.StartAsync(new MtSicsProtocol(Fast), transport);

        await run.Protocol.ZeroAsync(Ct);
        await run.Protocol.ZeroAsync(Ct);
        await run.WaitForReadingsAsync(run.Count + 3);

        transport.Writes.Count(w => w == "Z\r\n").ShouldBe(2);
    }

    [Fact]
    public async Task Zero_without_a_connection_is_unavailable_and_sends_nothing()
    {
        var transport = Scale(_ => ["Z A"]);
        var protocol = new MtSicsProtocol(Fast);

        var result = await protocol.ZeroAsync(Ct);

        result.Outcome.ShouldBe(ZeroOutcome.Unavailable);
        transport.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Zero_cancelled_before_it_is_sent_is_never_sent()
    {
        var transport = Scale(_ => ["Z A"]);
        await using var run = await Running.StartAsync(new MtSicsProtocol(Fast with { PollInterval = TimeSpan.FromMilliseconds(300) }), transport);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        var exception = await Record.ExceptionAsync(() => run.Protocol.ZeroAsync(cancelled.Token));
        await run.WaitForReadingsAsync(run.Count + 2);

        exception.ShouldBeAssignableTo<OperationCanceledException>();

        transport.Writes.ShouldNotContain("Z\r\n");
    }

    [Fact]
    public async Task Pending_zero_is_unavailable_when_the_connection_drops()
    {
        var transport = new FakeTransport
        {
            Responder = line => line == "SI" ? ["S S        572 g\r\n"] : null,
        };
        var protocol = new MtSicsProtocol(Fast with { MaxConsecutiveTimeouts = 1, ZeroTimeout = TimeSpan.FromSeconds(5) });
        await using var run = await Running.StartAsync(protocol, transport);

        var zero = protocol.ZeroAsync(Ct);
        await Task.Delay(50, Ct);
        transport.CloseRemote();

        (await zero.WaitAsync(TimeSpan.FromSeconds(10), Ct)).Outcome.ShouldBe(ZeroOutcome.Unavailable);
    }

    [Fact]
    public async Task Sir_stream_lines_before_the_answer_are_skipped_and_the_stream_is_requested_again()
    {
        var transport = new FakeTransport
        {
            Responder = line => line switch
            {
                "SIR" => ["S D        570 g\r\nS S        572 g\r\n"],
                "Z" => ["S S        572 g\r\nZ A\r\n"],
                _ => null,
            },
        };
        await using var run = await Running.StartAsync(new MtSicsProtocol(Fast with { Command = "SIR" }), transport);

        var result = await run.Protocol.ZeroAsync(Ct);
        await run.WaitForReadingsAsync(run.Count + 1);

        result.ShouldBe(ZeroResult.Done);
        var writes = transport.Writes.ToList();
        writes.Count(w => w == "Z\r\n").ShouldBe(1);
        writes[writes.IndexOf("Z\r\n") + 1].ShouldBe("SIR\r\n");
    }

    [Theory]
    [InlineData("S")]
    [InlineData("SI")]
    [InlineData("SIR")]
    [InlineData("Z")]
    public void Guard_allows_read_commands_and_zero(string command)
    {
        MtSicsCommands.IsSendable(command).ShouldBeTrue();
        Should.NotThrow(() => MtSicsCommands.EnsureSendable(command));
    }

    [Theory]
    [InlineData("T")]
    [InlineData("@")]
    [InlineData("TI")]
    [InlineData("TA")]
    [InlineData("TA 100 g")]
    [InlineData("TAC")]
    [InlineData("ZI")]
    [InlineData("z")]
    [InlineData("Z ")]
    [InlineData("Z\r\nT")]
    [InlineData("M01 1")]
    [InlineData("I4")]
    [InlineData("")]
    [InlineData(null)]
    public void Guard_rejects_tare_reset_and_every_other_command(string? command)
    {
        MtSicsCommands.IsSendable(command).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => MtSicsCommands.EnsureSendable(command));
    }

    [Fact]
    public void Zero_is_still_not_accepted_as_the_read_command()
    {
        Should.Throw<ArgumentException>(() => new MtSicsProtocolOptions { Command = "Z" });
        MtSicsProtocolOptions.AllowedCommands.ShouldBe(["S", "SI", "SIR"]);
    }

    /// <summary>
    /// A fake scale: answers SI with a weight and Z with the lines from <paramref name="zeroAnswer"/> (null means silence).
    /// It records how many commands were written without an answer having been read in between.
    /// </summary>
    private static CountingTransport Scale(Func<int, IEnumerable<string>?> zeroAnswer)
    {
        var zeros = 0;
        return new CountingTransport(line => line switch
        {
            "SI" => ["S S        572 g"],
            "Z" => zeroAnswer(++zeros),
            _ => null,
        });
    }

    /// <summary>Wraps <see cref="FakeTransport"/> and tracks commands that are written while an answer is still unread.</summary>
    private sealed class CountingTransport : ITransport
    {
        private readonly FakeTransport _inner;
        private int _outstanding;
        private int _maxOutstanding;

        public CountingTransport(Func<string, IEnumerable<string>?> respond)
        {
            _inner = new FakeTransport
            {
                Responder = line => respond(line)?.Select(l => l + "\r\n").ToList(),
            };
        }

        public string Description => _inner.Description;

        public IReadOnlyList<string> Writes => _inner.Writes;

        public int MaxOutstanding => Volatile.Read(ref _maxOutstanding);

        public Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

        public Task CloseAsync() => _inner.CloseAsync();

        public ValueTask DisposeAsync() => _inner.DisposeAsync();

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            var outstanding = Interlocked.Increment(ref _outstanding);
            InterlockedMax(ref _maxOutstanding, outstanding);
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var count = await _inner.ReadAsync(buffer, cancellationToken);
            if (buffer.Span[..count].Contains((byte)'\n'))
            {
                Interlocked.Exchange(ref _outstanding, 0);
            }

            return count;
        }

        public void DiscardInput()
        {
            Interlocked.Exchange(ref _outstanding, 0);
            _inner.DiscardInput();
        }

        private static void InterlockedMax(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var previous = Interlocked.CompareExchange(ref target, value, current);
                if (previous == current)
                {
                    return;
                }

                current = previous;
            }
        }
    }

    /// <summary>A protocol reading in the background until disposed.</summary>
    private sealed class Running : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Channel<WeightReading> _readings = Channel.CreateUnbounded<WeightReading>();
        private readonly Task _loop;
        private int _count;

        private Running(MtSicsProtocol protocol, ITransport transport)
        {
            Protocol = protocol;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            _cts.CancelAfter(TimeSpan.FromSeconds(20));
            _loop = Task.Run(async () =>
            {
                try
                {
                    await foreach (var reading in protocol.ReadAsync(transport, _cts.Token))
                    {
                        Interlocked.Increment(ref _count);
                        _readings.Writer.TryWrite(reading);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (DeviceConnectionLostException)
                {
                }
            });
        }

        public MtSicsProtocol Protocol { get; }

        public int Count => Volatile.Read(ref _count);

        public static async Task<Running> StartAsync(MtSicsProtocol protocol, ITransport transport)
        {
            var run = new Running(protocol, transport);
            await run.WaitForReadingsAsync(1);
            return run;
        }

        public async Task WaitForReadingsAsync(int count)
        {
            while (Count < count)
            {
                await _readings.Reader.WaitToReadAsync(_cts.Token);
                while (_readings.Reader.TryRead(out _))
                {
                }

                if (Count < count)
                {
                    await Task.Delay(1, _cts.Token);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            await _loop;
            _cts.Dispose();
        }
    }
}
