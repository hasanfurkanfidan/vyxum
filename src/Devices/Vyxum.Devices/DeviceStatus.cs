namespace Vyxum.Devices;

/// <summary>The connection state of a device, as seen by a <see cref="DeviceRunner"/>.</summary>
public enum DeviceStatus
{
    /// <summary>The transport is being opened, or it is open and no reading has arrived yet.</summary>
    Connecting,

    /// <summary>At least one reading arrived since the transport was opened.</summary>
    Connected,

    /// <summary>The transport is closed. The runner waits before the next attempt, or has stopped.</summary>
    Disconnected,
}

/// <summary>A status change reported by a <see cref="DeviceRunner"/>.</summary>
/// <param name="DeviceId">The device the status belongs to.</param>
/// <param name="Status">The new status.</param>
/// <param name="LastError">The message of the error that caused a disconnect; null otherwise.</param>
/// <param name="RetryIn">When disconnected after an error, the wait before the next attempt; null otherwise.</param>
/// <param name="Timestamp">When the status changed.</param>
public sealed record DeviceStatusInfo(
    string DeviceId,
    DeviceStatus Status,
    string? LastError,
    TimeSpan? RetryIn,
    DateTimeOffset Timestamp);
