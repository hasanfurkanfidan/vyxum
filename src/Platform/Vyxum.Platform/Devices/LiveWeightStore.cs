using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// In-memory <see cref="ILiveWeightSource"/> fed by the agent hub. Keeps the latest weight and
/// status of every device and remembers which agent connection serves it.
/// </summary>
public sealed class LiveWeightStore(TimeProvider time) : ILiveWeightSource
{
    private const int WatcherCapacity = 16;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, DeviceEntry> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _agentNames = new(StringComparer.Ordinal);

    /// <summary>Records an agent connection and the devices it serves; new devices start as connecting.</summary>
    public void RegisterAgent(string connectionId, string agentName, IEnumerable<string> deviceIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(deviceIds);

        var now = time.GetUtcNow();
        lock (_lock)
        {
            _agentNames[connectionId] = agentName;
            foreach (var deviceId in deviceIds)
            {
                var before = Snapshot(deviceId);
                var entry = Claim(connectionId, deviceId, now);
                if (entry.State == DeviceConnectionState.Disconnected)
                {
                    entry.State = DeviceConnectionState.Connecting;
                }

                PublishIfChanged(entry, before);
            }
        }
    }

    /// <summary>Stores a reading; the device becomes connected. Returns the stored weight.</summary>
    public LiveWeight ReportReading(string connectionId, string deviceId, AgentReading reading)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(reading);

        var now = time.GetUtcNow();
        lock (_lock)
        {
            var before = Snapshot(deviceId);
            var entry = Claim(connectionId, deviceId, now);
            var weight = new LiveWeight(
                entry.DeviceId, reading.Value, reading.Unit, reading.State, reading.Tare, reading.IsNet, reading.Timestamp, now);
            entry.LastWeight = weight;
            entry.State = DeviceConnectionState.Connected;
            entry.LastError = null;
            foreach (var watcher in entry.Watchers)
            {
                watcher.Writer.TryWrite(weight);
            }

            PublishIfChanged(entry, before);
            return weight;
        }
    }

    /// <summary>Stores a status change reported by the agent.</summary>
    public void ReportStatus(string connectionId, string deviceId, AgentDeviceStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(status);

        var now = time.GetUtcNow();
        lock (_lock)
        {
            var before = Snapshot(deviceId);
            var entry = Claim(connectionId, deviceId, now);
            entry.State = status.Status;
            entry.LastError = status.LastError;
            PublishIfChanged(entry, before);
        }
    }

    /// <summary>Marks every device served by the connection as disconnected. Returns how many there were.</summary>
    public int DisconnectAgent(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        lock (_lock)
        {
            _agentNames.Remove(connectionId);
            var count = 0;
            foreach (var entry in _devices.Values.Where(e => e.ConnectionId == connectionId))
            {
                var before = entry.ToSnapshot();
                entry.ConnectionId = null;
                entry.State = DeviceConnectionState.Disconnected;
                PublishIfChanged(entry, before);
                count++;
            }

            return count;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<LiveDevice> GetDevices()
    {
        lock (_lock)
        {
            return [.. _devices.Values.Where(e => e.Known).OrderBy(e => e.DeviceId, StringComparer.Ordinal).Select(e => e.ToSnapshot())];
        }
    }

    /// <inheritdoc />
    public LiveDevice? GetDevice(string deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        lock (_lock)
        {
            return _devices.TryGetValue(deviceId, out var entry) && entry.Known ? entry.ToSnapshot() : null;
        }
    }

    /// <inheritdoc />
    public LiveWeight? GetLatest(string deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        lock (_lock)
        {
            return _devices.TryGetValue(deviceId, out var entry) ? entry.LastWeight : null;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LiveWeight> WatchAsync(
        string deviceId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var channel = Channel.CreateBounded<LiveWeight>(new BoundedChannelOptions(WatcherCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        DeviceEntry entry;
        lock (_lock)
        {
            entry = GetOrAdd(deviceId, lastSeen: null);
            entry.Watchers.Add(channel);
            if (entry.LastWeight is { } latest)
            {
                channel.Writer.TryWrite(latest);
            }
        }

        try
        {
            while (true)
            {
                LiveWeight weight;
                try
                {
                    weight = await channel.Reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                yield return weight;
            }
        }
        finally
        {
            lock (_lock)
            {
                entry.Watchers.Remove(channel);
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LiveDevice> WatchDeviceAsync(
        string deviceId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var channel = Channel.CreateBounded<LiveDevice>(new BoundedChannelOptions(WatcherCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        DeviceEntry entry;
        lock (_lock)
        {
            entry = GetOrAdd(deviceId, lastSeen: null);
            entry.DeviceWatchers.Add(channel);
            channel.Writer.TryWrite(entry.ToSnapshot());
        }

        try
        {
            while (true)
            {
                LiveDevice device;
                try
                {
                    device = await channel.Reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                yield return device;
            }
        }
        finally
        {
            lock (_lock)
            {
                entry.DeviceWatchers.Remove(channel);
            }
        }
    }

    /// <summary>
    /// The current snapshot of an entry that has device watchers; null otherwise, so that readings
    /// nobody watches cost nothing extra. Call under the lock.
    /// </summary>
    private LiveDevice? Snapshot(string deviceId) =>
        deviceId is not null && _devices.TryGetValue(deviceId, out var entry) && entry.DeviceWatchers.Count > 0
            ? entry.ToSnapshot()
            : null;

    /// <summary>Sends the entry's snapshot to its device watchers when more than the timestamps changed. Call under the lock.</summary>
    private static void PublishIfChanged(DeviceEntry entry, LiveDevice? before)
    {
        if (entry.DeviceWatchers.Count == 0)
        {
            return;
        }

        var after = entry.ToSnapshot();
        if (before is not null && WithoutTimes(before) == WithoutTimes(after))
        {
            return;
        }

        foreach (var watcher in entry.DeviceWatchers)
        {
            watcher.Writer.TryWrite(after);
        }
    }

    private static LiveDevice WithoutTimes(LiveDevice device) => device with
    {
        LastSeen = default,
        LastWeight = device.LastWeight is { } weight ? weight with { Timestamp = default, ReceivedAt = default } : null,
    };

    private DeviceEntry Claim(string connectionId, string deviceId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var entry = GetOrAdd(deviceId, now);
        entry.ConnectionId = connectionId;
        entry.AgentName = _agentNames.GetValueOrDefault(connectionId) ?? entry.AgentName;
        entry.LastSeen = now;
        entry.Known = true;
        return entry;
    }

    private DeviceEntry GetOrAdd(string deviceId, DateTimeOffset? lastSeen)
    {
        if (!_devices.TryGetValue(deviceId, out var entry))
        {
            entry = new DeviceEntry(deviceId) { LastSeen = lastSeen ?? default };
            _devices.Add(deviceId, entry);
        }

        return entry;
    }

    private sealed class DeviceEntry(string deviceId)
    {
        public string DeviceId { get; } = deviceId;

        public string? ConnectionId { get; set; }

        public string? AgentName { get; set; }

        public DeviceConnectionState State { get; set; } = DeviceConnectionState.Disconnected;

        public string? LastError { get; set; }

        public LiveWeight? LastWeight { get; set; }

        public DateTimeOffset LastSeen { get; set; }

        /// <summary>False while the entry exists only because someone watches it.</summary>
        public bool Known { get; set; }

        public List<Channel<LiveWeight>> Watchers { get; } = [];

        public List<Channel<LiveDevice>> DeviceWatchers { get; } = [];

        public LiveDevice ToSnapshot() => new(DeviceId, AgentName, State, LastError, LastWeight, LastSeen);
    }
}
