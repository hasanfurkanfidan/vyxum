using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Tests;

/// <summary>An <see cref="ILiveWeightSource"/> whose devices and readings the test sets.</summary>
internal sealed class FakeLiveWeightSource : ILiveWeightSource
{
    private readonly ConcurrentDictionary<string, LiveDevice> _devices = new(StringComparer.Ordinal);

    /// <summary>Sets the latest reading of a connected device.</summary>
    public void Set(
        string deviceId,
        decimal? value,
        string? unit = "kg",
        LiveWeightState state = LiveWeightState.Stable,
        decimal? tare = null,
        bool? isNet = null,
        DeviceConnectionState connection = DeviceConnectionState.Connected)
    {
        var now = DateTimeOffset.UtcNow;
        var weight = new LiveWeight(deviceId, value, unit, state, tare, isNet, now, now);
        _devices[deviceId] = new LiveDevice(deviceId, "agent", connection, null, weight, now);
    }

    /// <summary>Makes a device known without any reading.</summary>
    public void SetWithoutReading(string deviceId, DeviceConnectionState connection = DeviceConnectionState.Connecting) =>
        _devices[deviceId] = new LiveDevice(deviceId, "agent", connection, null, null, DateTimeOffset.UtcNow);

    public IReadOnlyList<LiveDevice> GetDevices() => [.. _devices.Values.OrderBy(d => d.DeviceId, StringComparer.Ordinal)];

    public LiveDevice? GetDevice(string deviceId) => _devices.TryGetValue(deviceId, out var device) ? device : null;

    public LiveWeight? GetLatest(string deviceId) => GetDevice(deviceId)?.LastWeight;

    public async IAsyncEnumerable<LiveWeight> WatchAsync(string deviceId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (GetLatest(deviceId) is { } latest)
        {
            yield return latest;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public async IAsyncEnumerable<LiveDevice> WatchDeviceAsync(string deviceId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (GetDevice(deviceId) is { } device)
        {
            yield return device;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
