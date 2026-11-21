namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Live weights of the connected devices. Readings are kept in memory only; they are not
/// integration events (decision V-27).
/// </summary>
public interface ILiveWeightSource
{
    /// <summary>All devices known since startup, ordered by id.</summary>
    IReadOnlyList<LiveDevice> GetDevices();

    /// <summary>The device with the given id, or null when it is unknown.</summary>
    LiveDevice? GetDevice(string deviceId);

    /// <summary>The latest weight of the device, or null when none has arrived.</summary>
    LiveWeight? GetLatest(string deviceId);

    /// <summary>
    /// Yields the latest weight of the device (if any) and then every new one, until
    /// <paramref name="cancellationToken"/> is cancelled. A slow consumer skips older weights.
    /// </summary>
    IAsyncEnumerable<LiveWeight> WatchAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yields a snapshot of the device at once and then a new one whenever its connection state,
    /// last error, agent or weight (value, unit, state, tare, net flag) changes, until
    /// <paramref name="cancellationToken"/> is cancelled. Repeated identical readings yield nothing.
    /// A device that is not known yet yields a waiting snapshot (state
    /// <see cref="DeviceConnectionState.Disconnected"/>, no agent, no weight, default
    /// <see cref="LiveDevice.LastSeen"/>) until its agent reports it. A slow consumer skips older snapshots.
    /// </summary>
    IAsyncEnumerable<LiveDevice> WatchDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
}
