namespace Vyxum.Devices;

/// <summary>
/// What a probe found: the protocol a device speaks, the protocol settings to read it with, and one sample reading.
/// </summary>
/// <param name="ProtocolType">The protocol type as used in device settings, for example <c>mt-sics</c>.</param>
/// <param name="ProtocolName">A short display name of the protocol, for example <c>MT-SICS</c>.</param>
/// <param name="ProtocolSettings">
/// Settings beyond the protocol defaults, keyed by the camel-case name of the device setting (for example
/// <c>command</c> or <c>checksumEnabled</c>). Values are strings, booleans or numbers.
/// </param>
/// <param name="Reading">One reading taken while probing; null when the device answered without a weight (busy).</param>
/// <param name="SerialNumber">The serial number when the device reported it; otherwise null.</param>
public sealed record ProbeMatch(
    string ProtocolType,
    string ProtocolName,
    IReadOnlyDictionary<string, object> ProtocolSettings,
    WeightReading? Reading,
    string? SerialNumber = null);

/// <summary>
/// Recognises a protocol from the bytes a device sent on its own, without anything being written to it. Implemented by
/// protocol packages, so that knowledge of a wire format stays in its package.
/// </summary>
public interface IPassiveProbe
{
    /// <summary>
    /// Looks at the bytes received while listening (possibly starting in the middle of a frame) and returns a match when
    /// they are clearly this protocol; otherwise null. Never throws on bad input.
    /// </summary>
    ProbeMatch? Recognise(ReadOnlySpan<byte> received, DateTimeOffset timestamp);
}

/// <summary>Timing for an <see cref="IActiveProbe"/>.</summary>
/// <param name="ResponseTimeout">How long to wait for each answer.</param>
/// <param name="ResendOnSilence">
/// True to send a command once more when the first attempt got no answer at all. An unclear answer (an error line or
/// noise) is always followed by one more attempt.
/// </param>
/// <param name="TimeProvider">The clock.</param>
public sealed record ProbeContext(TimeSpan ResponseTimeout, bool ResendOnSilence, TimeProvider TimeProvider);

/// <summary>
/// Asks a device which protocol it speaks by sending read-only commands. Implemented by protocol packages. A probe may
/// write nothing but the byte sequences in <see cref="AllowedWrites"/>; the caller enforces this and fails any other write.
/// </summary>
public interface IActiveProbe
{
    /// <summary>
    /// Every byte sequence the probe may write, each exactly as written (command plus terminator). Only commands that read
    /// and change nothing on the device may be listed (decision V-28).
    /// </summary>
    IReadOnlyList<byte[]> AllowedWrites { get; }

    /// <summary>
    /// Probes the device behind an open transport. Returns a match, or null when the device did not answer like this
    /// protocol. Throws only for cancellation or a dead transport.
    /// </summary>
    Task<ProbeMatch?> ProbeAsync(ITransport transport, ProbeContext context, CancellationToken cancellationToken);
}
