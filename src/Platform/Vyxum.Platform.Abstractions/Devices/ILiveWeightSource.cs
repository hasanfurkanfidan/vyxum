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
}
