namespace Vyxum.Devices.ToledoContinuous;

/// <summary>Settings for <see cref="ToledoContinuousProtocol"/>.</summary>
public sealed record ToledoContinuousOptions
{
    /// <summary>True when the indicator sends a checksum byte after CR. Frames with a wrong checksum are dropped. Default: false.</summary>
    public bool ChecksumEnabled { get; init; }

    /// <summary>When no valid frame arrives for this long, the link is considered dead. Default: 3 s.</summary>
    public TimeSpan InactivityTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// After this many invalid frames in a row the link is considered dead. A steady stream of invalid frames
    /// usually means a wrong baud rate, parity or checksum setting. Default: 20.
    /// </summary>
    public int MaxConsecutiveInvalidFrames
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 20;
}
