namespace Vyxum.Devices.Simulator;

/// <summary>
/// A simulated device. It talks bytes only: it implements the wire format of a device and knows nothing about the
/// Vyxum protocol classes. One call to <see cref="RunSessionAsync"/> is one connection (one opened transport,
/// one TCP client).
/// </summary>
public interface ISimulatedDevice
{
    /// <summary>
    /// Serves one connection until the stream is closed by the other side or <paramref name="cancellationToken"/> is cancelled.
    /// Per-connection state (for example "the first command") belongs to the call, so one device object can serve many sessions.
    /// </summary>
    Task RunSessionAsync(Stream stream, CancellationToken cancellationToken);
}
