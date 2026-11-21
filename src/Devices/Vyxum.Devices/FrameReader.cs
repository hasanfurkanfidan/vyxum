namespace Vyxum.Devices;

/// <summary>
/// Splits the byte stream of an <see cref="ITransport"/> into frames. Handles frames split across reads
/// and several frames in one read. Not thread-safe; one reader per transport.
/// </summary>
public sealed class FrameReader
{
    private const byte Stx = 0x02;
    private const byte Cr = 0x0D;
    private const byte Lf = 0x0A;

    private readonly ITransport _transport;
    private readonly FrameReaderOptions _options;
    private readonly byte[] _readBuffer = new byte[512];
    private readonly List<byte> _payload = [];
    private readonly List<byte> _trailer = [];
    private int _readPosition;
    private int _readLength;
    private State _state;

    /// <summary>Creates a reader over <paramref name="transport"/>.</summary>
    public FrameReader(ITransport transport, FrameReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _options = options ?? new FrameReaderOptions();
        _state = InitialState;
    }

    /// <summary>The number of frames discarded because they were longer than <see cref="FrameReaderOptions.MaxFrameLength"/>.</summary>
    public int OverflowCount { get; private set; }

    private State InitialState => _options.Mode == FrameMode.StxCr ? State.Hunting : State.InFrame;

    /// <summary>
    /// Reads the next frame. Returns null when the remote side closed the connection.
    /// Cancel <paramref name="cancellationToken"/> to apply a timeout; a partial frame is kept for the next call.
    /// </summary>
    public async ValueTask<Frame?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            while (_readPosition < _readLength)
            {
                var frame = Feed(_readBuffer[_readPosition++]);
                if (frame is not null)
                {
                    return frame;
                }
            }

            var count = await _transport.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return null;
            }

            _readPosition = 0;
            _readLength = count;
        }
    }

    /// <summary>Reads the next frame as text. Returns null when the remote side closed the connection.</summary>
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        return frame?.Text;
    }

    /// <summary>Drops buffered bytes and any partial frame.</summary>
    public void Reset()
    {
        _readPosition = 0;
        _readLength = 0;
        _payload.Clear();
        _trailer.Clear();
        _state = InitialState;
    }

    private Frame? Feed(byte value) => _options.Mode == FrameMode.StxCr ? FeedStx(value) : FeedLine(value);

    private Frame? FeedLine(byte value)
    {
        if (IsLineEnd(value))
        {
            if (_options.Terminator == LineTerminator.CrLf)
            {
                _payload.RemoveAt(_payload.Count - 1);
            }

            var discarding = _state == State.Discarding;
            _state = State.InFrame;
            if (discarding || _payload.Count == 0)
            {
                _payload.Clear();
                return null;
            }

            return TakeFrame();
        }

        if (_state == State.Discarding)
        {
            // Keep only the last byte so that a CR LF split around the overflow is still recognised.
            _payload.Clear();
            _payload.Add(value);
            return null;
        }

        _payload.Add(value);
        var limit = _options.Terminator == LineTerminator.CrLf ? _options.MaxFrameLength + 1 : _options.MaxFrameLength;
        if (_payload.Count > limit)
        {
            OverflowCount++;
            _state = State.Discarding;
            _payload.Clear();
            _payload.Add(value);
        }

        return null;
    }

    private bool IsLineEnd(byte value) => _options.Terminator switch
    {
        LineTerminator.CrLf => value == Lf && _payload.Count > 0 && _payload[^1] == Cr,
        LineTerminator.Lf => value == Lf,
        LineTerminator.Cr => value == Cr,
        _ => value is Cr or Lf,
    };

    private Frame? FeedStx(byte value)
    {
        switch (_state)
        {
            case State.Hunting:
                if (value == Stx)
                {
                    _payload.Clear();
                    _trailer.Clear();
                    _state = State.InFrame;
                }

                return null;

            case State.InFrame:
                if (value == Stx)
                {
                    // A new frame started before the previous one ended: resynchronise on it.
                    _payload.Clear();
                    return null;
                }

                if (value == Cr)
                {
                    if (_options.TrailingByteCount == 0)
                    {
                        _state = State.Hunting;
                        return TakeFrame();
                    }

                    _state = State.Trailer;
                    return null;
                }

                _payload.Add(value);
                if (_payload.Count > _options.MaxFrameLength)
                {
                    OverflowCount++;
                    _payload.Clear();
                    _state = State.Hunting;
                }

                return null;

            default:
                _trailer.Add(value);
                if (_trailer.Count < _options.TrailingByteCount)
                {
                    return null;
                }

                _state = State.Hunting;
                return TakeFrame();
        }
    }

    private Frame TakeFrame()
    {
        var frame = new Frame([.. _payload], [.. _trailer]);
        _payload.Clear();
        _trailer.Clear();
        return frame;
    }

    private enum State
    {
        Hunting,
        InFrame,
        Discarding,
        Trailer,
    }
}
