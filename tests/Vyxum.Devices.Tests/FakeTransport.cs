using System.Text;
using System.Threading.Channels;

namespace Vyxum.Devices.Tests;

/// <summary>An in-memory transport. Tests push bytes in, and can answer each written line through <see cref="Responder"/>.</summary>
internal sealed class FakeTransport : ITransport
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly Lock _lock = new();
    private readonly List<string> _writes = [];
    private ReadOnlyMemory<byte> _current;

    public string Description { get; init; } = "fake";

    /// <summary>Called with each written line (without CR LF); the returned strings are queued as incoming data. Null means silence.</summary>
    public Func<string, IEnumerable<string>?>? Responder { get; init; }

    /// <summary>Thrown by <see cref="OpenAsync"/> when set.</summary>
    public Exception? OpenException { get; init; }

    public int OpenCount { get; private set; }

    public int CloseCount { get; private set; }

    public int DisposeCount { get; private set; }

    public int DiscardInputCount { get; private set; }

    public IReadOnlyList<string> Writes
    {
        get
        {
            lock (_lock)
            {
                return [.. _writes];
            }
        }
    }

    public void Push(string text) => Push(Encoding.Latin1.GetBytes(text));

    public void Push(params byte[] bytes) => _incoming.Writer.TryWrite(bytes);

    /// <summary>Simulates the remote side closing the connection: reads return 0 once queued data is consumed.</summary>
    public void CloseRemote() => _incoming.Writer.TryComplete();

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        OpenCount++;
        return OpenException is null ? Task.CompletedTask : Task.FromException(OpenException);
    }

    public Task CloseAsync()
    {
        CloseCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var text = Encoding.Latin1.GetString(buffer.Span);
        lock (_lock)
        {
            _writes.Add(text);
        }

        if (Responder is not null)
        {
            foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var response in Responder(line) ?? [])
                {
                    Push(response);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_current.IsEmpty)
        {
            try
            {
                _current = await _incoming.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return 0;
            }
        }

        var count = Math.Min(buffer.Length, _current.Length);
        _current[..count].CopyTo(buffer);
        _current = _current[count..];
        return count;
    }

    public void DiscardInput()
    {
        DiscardInputCount++;
        _current = ReadOnlyMemory<byte>.Empty;
        while (_incoming.Reader.TryRead(out _))
        {
        }
    }
}
