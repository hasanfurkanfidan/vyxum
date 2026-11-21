namespace Vyxum.Devices;

/// <summary>How a <see cref="FrameReader"/> finds the start and end of a frame.</summary>
public enum FrameMode
{
    /// <summary>Frames are lines that end with a terminator (see <see cref="LineTerminator"/>).</summary>
    Line,

    /// <summary>Frames start with STX (0x02) and end with CR (0x0D), optionally followed by trailing bytes such as a checksum.</summary>
    StxCr,
}

/// <summary>The terminator of a line in <see cref="FrameMode.Line"/>.</summary>
public enum LineTerminator
{
    /// <summary>CR LF (0x0D 0x0A).</summary>
    CrLf,

    /// <summary>A lone LF (0x0A).</summary>
    Lf,

    /// <summary>A lone CR (0x0D).</summary>
    Cr,

    /// <summary>Any of CR, LF or CR LF. Empty lines are skipped.</summary>
    Any,
}

/// <summary>Settings for a <see cref="FrameReader"/>.</summary>
public sealed record FrameReaderOptions
{
    /// <summary>Line or STX framing. Default: line.</summary>
    public FrameMode Mode { get; init; } = FrameMode.Line;

    /// <summary>The line terminator in line mode. Default: CR LF.</summary>
    public LineTerminator Terminator { get; init; } = LineTerminator.CrLf;

    /// <summary>In STX mode, the number of bytes after CR that belong to the frame (for example 1 for a checksum). Default: 0.</summary>
    public int TrailingByteCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// The largest payload accepted. A longer frame is discarded and the reader resynchronises on the next
    /// terminator (line mode) or STX (STX mode). Default: 256.
    /// </summary>
    public int MaxFrameLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 256;
}
