using System.Threading.Channels;

namespace Vyxum.Devices.Simulator;

/// <summary>
/// One end of an in-memory duplex pipe: reads come from one channel, writes go to another.
/// Reads return 0 once the other side has completed its channel; writes then throw <see cref="IOException"/>.
/// </summary>
internal sealed class DuplexStream : Stream
{
    private readonly Channel<byte[]> _incoming;
    private readonly Channel<byte[]> _outgoing;
    private readonly Func<bool>? _dropWrites;
    private byte[]? _pending;
    private int _offset;

    public DuplexStream(Channel<byte[]> incoming, Channel<byte[]> outgoing, Func<bool>? dropWrites = null)
    {
        _incoming = incoming;
        _outgoing = outgoing;
        _dropWrites = dropWrites;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public static (DuplexStream Host, DuplexStream Device, Channel<byte[]> ToDevice, Channel<byte[]> ToHost) CreatePair(Func<bool>? dropDeviceWrites)
    {
        var toDevice = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        var toHost = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        return (new DuplexStream(toHost, toDevice), new DuplexStream(toDevice, toHost, dropDeviceWrites), toDevice, toHost);
    }

    /// <summary>Drops bytes that were received and not read yet.</summary>
    public void DiscardPending()
    {
        _pending = null;
        while (_incoming.Reader.TryRead(out _))
        {
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_pending is null)
        {
            try
            {
                _pending = await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return 0;
            }

            _offset = 0;
        }

        var count = Math.Min(buffer.Length, _pending.Length - _offset);
        _pending.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        if (_offset >= _pending.Length)
        {
            _pending = null;
        }

        return count;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_dropWrites?.Invoke() == true)
        {
            return ValueTask.CompletedTask;
        }

        if (!_outgoing.Writer.TryWrite(buffer.ToArray()))
        {
            throw new IOException("The simulated connection is closed.");
        }

        return ValueTask.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _outgoing.Writer.TryComplete();
        }

        base.Dispose(disposing);
    }
}
