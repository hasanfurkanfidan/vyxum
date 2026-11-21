using System.Text;

namespace Vyxum.Devices.Simulator;

/// <summary>Reads text lines from a stream. LF ends a line; CR is dropped. Returns null at the end of the stream.</summary>
internal sealed class LineReader(Stream stream)
{
    private readonly byte[] _buffer = new byte[256];
    private readonly StringBuilder _line = new();
    private int _start;
    private int _end;

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            while (_start < _end)
            {
                var b = _buffer[_start++];
                if (b == (byte)'\n')
                {
                    var line = _line.ToString();
                    _line.Clear();
                    return line;
                }

                if (b != (byte)'\r')
                {
                    _line.Append((char)b);
                }
            }

            var read = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            _start = 0;
            _end = read;
        }
    }
}

/// <summary>Writes whole lines (text plus CR LF) to a stream, one at a time.</summary>
internal sealed class LineWriter(Stream stream, string terminator = "\r\n")
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        var bytes = Encoding.Latin1.GetBytes(line + terminator);
        await WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
