namespace Vyxum.Devices;

/// <summary>The outcome of a zero command sent to a device.</summary>
public enum ZeroOutcome
{
    /// <summary>The device confirmed that it zeroed.</summary>
    Done,

    /// <summary>The device answered but did not zero (for example the weight is in motion or out of the zero range).</summary>
    Rejected,

    /// <summary>The device is not connected or did not answer in time.</summary>
    Unavailable,
}

/// <summary>The result of <see cref="ISupportsZero.ZeroAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Detail">A short English explanation for logs; null when there is nothing to add.</param>
public sealed record ZeroResult(ZeroOutcome Outcome, string? Detail = null)
{
    /// <summary>The device zeroed.</summary>
    public static ZeroResult Done { get; } = new(ZeroOutcome.Done);

    /// <summary>The device answered but did not zero.</summary>
    public static ZeroResult Rejected(string detail) => new(ZeroOutcome.Rejected, detail);

    /// <summary>The device could not be reached.</summary>
    public static ZeroResult Unavailable(string detail) => new(ZeroOutcome.Unavailable, detail);
}

/// <summary>
/// Implemented by a protocol that can ask the device to set its zero point. The command is sent once per call, on the
/// connection the protocol is reading from, between two read commands (never interleaved with them).
/// </summary>
public interface ISupportsZero
{
    /// <summary>
    /// Asks the device to zero and waits for its answer. Returns <see cref="ZeroOutcome.Unavailable"/> at once when no
    /// connection is being read. When <paramref name="cancellationToken"/> is cancelled before the command was sent, it
    /// is not sent at all.
    /// </summary>
    Task<ZeroResult> ZeroAsync(CancellationToken cancellationToken);
}
