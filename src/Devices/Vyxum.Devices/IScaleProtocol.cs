namespace Vyxum.Devices;

/// <summary>Turns the bytes of a transport into weight readings for one protocol (MT-SICS, Toledo Continuous, ...).</summary>
public interface IScaleProtocol
{
    /// <summary>
    /// Reads weights from an open transport until cancelled. Throws (for example <see cref="DeviceConnectionLostException"/>)
    /// when the protocol decides the link is dead.
    /// </summary>
    IAsyncEnumerable<WeightReading> ReadAsync(ITransport transport, CancellationToken cancellationToken);
}
