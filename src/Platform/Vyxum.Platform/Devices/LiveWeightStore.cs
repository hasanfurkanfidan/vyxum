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
    private readonly Dictionary<string, HashSet<string>> _registeredDevices = new(StringComparer.Ordinal);

    /// <summary>
    /// Records an agent connection, the devices it serves and the commands each supports; new devices start as connecting.
    /// A device missing from <paramref name="deviceCommands"/> supports no command.
    /// </summary>
    /// <remarks>
    /// A device that a connected agent with another name already serves stays with that agent: two agents reporting
    /// the same device id would otherwise make the device jump between two scales. The new connection gets the device
    /// once the other agent disconnects. An agent with the same name (the same agent after a restart, before its old
    /// connection timed out) takes the device over.
    /// </remarks>
    /// <returns>The listed devices that stay with another agent, with that agent's name.</returns>
    public IReadOnlyList<(string DeviceId, string HeldBy)> RegisterAgent(
        string connectionId,
        string agentName,
        IEnumerable<string> deviceIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? deviceCommands = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(deviceIds);

        var ids = deviceIds as IReadOnlyCollection<string> ?? [.. deviceIds];
        var now = time.GetUtcNow();
        var conflicts = new List<(string DeviceId, string HeldBy)>();
        lock (_lock)
        {
            _agentNames[connectionId] = agentName;
            _registeredDevices[connectionId] = new HashSet<string>(ids, StringComparer.Ordinal);

            // A registration lists every device the agent serves now; the ones it dropped are released.
            var listed = new HashSet<string>(ids, StringComparer.Ordinal);
            foreach (var dropped in _devices.Values.Where(e => e.ConnectionId == connectionId && !listed.Contains(e.DeviceId)).ToList())
            {
                var snapshot = dropped.DeviceWatchers.Count > 0 ? dropped.ToSnapshot() : null;
                dropped.ConnectionId = null;
                dropped.State = DeviceConnectionState.Disconnected;
                dropped.LastError = null;
                dropped.LastWeight = null;
                dropped.SupportedCommands = [];
                dropped.Known = false;
                PublishIfChanged(dropped, snapshot);
            }

            foreach (var deviceId in ids)
            {
                if (HeldByOtherAgent(deviceId, agentName) is { } heldBy)
                {
                    conflicts.Add((deviceId, heldBy));
                    continue;
                }

                var before = Snapshot(deviceId);
                var entry = Claim(connectionId, deviceId, now);
                if (entry.State == DeviceConnectionState.Disconnected)
                {
                    entry.State = DeviceConnectionState.Connecting;
                }

                var commands = deviceCommands?.GetValueOrDefault(deviceId) ?? [];
                if (!entry.SupportedCommands.SequenceEqual(commands, StringComparer.Ordinal))
                {
                    // A new list only when it changed, so that unchanged snapshots stay equal.
                    entry.SupportedCommands = [.. commands.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal)];
                }

                PublishIfChanged(entry, before);
            }
        }

        return conflicts;
    }

    /// <summary>
    /// True when the connection serves the device: it listed the device when it registered and no other agent holds it.
    /// Readings and status changes from any other connection are not stored.
    /// </summary>
    public bool Serves(string connectionId, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(deviceId);

        lock (_lock)
        {
            return _devices.TryGetValue(deviceId, out var entry) && entry.ConnectionId == connectionId;
        }
    }

    /// <summary>The name an agent connection registered with, or null when it has not registered.</summary>
    public string? GetAgentName(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        lock (_lock)
        {
            return _agentNames.GetValueOrDefault(connectionId);
        }
    }

    /// <summary>The registered agent connections and their names.</summary>
    public IReadOnlyList<(string ConnectionId, string AgentName)> GetAgents()
    {
        lock (_lock)
        {
            return [.. _agentNames.Select(a => (a.Key, a.Value))];
        }
    }

    /// <summary>
    /// Replaces the details from the device settings (name, capacity, division, unit). A configured device is listed
    /// even before an agent reports it (disconnected, without agent name).
    /// </summary>
    public void SetConfiguredDevices(IEnumerable<ConfiguredDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var byId = new Dictionary<string, ConfiguredDevice>(StringComparer.Ordinal);
        foreach (var device in devices)
        {
            byId[device.Id] = device;
        }

        lock (_lock)
        {
            foreach (var entry in _devices.Values)
            {
                var config = byId.GetValueOrDefault(entry.DeviceId);
                if (config != entry.Config)
                {
                    var before = entry.DeviceWatchers.Count > 0 ? entry.ToSnapshot() : null;
                    entry.Config = config;
                    PublishIfChanged(entry, before);
                }
            }

            foreach (var config in byId.Values.Where(c => !_devices.ContainsKey(c.Id)))
            {
                GetOrAdd(config.Id, lastSeen: null).Config = config;
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

        var now = time.GetUtcNow();
        lock (_lock)
        {
            _agentNames.Remove(connectionId);
            _registeredDevices.Remove(connectionId);
            var count = 0;
            foreach (var entry in _devices.Values.Where(e => e.ConnectionId == connectionId).ToList())
            {
                var before = entry.ToSnapshot();
                entry.ConnectionId = null;
                entry.State = DeviceConnectionState.Disconnected;

                // Another connected agent that listed the device and waited for it gets it now.
                if (_registeredDevices.FirstOrDefault(r => r.Value.Contains(entry.DeviceId)).Key is { } waiting)
                {
                    Claim(waiting, entry.DeviceId, now);
                    entry.State = DeviceConnectionState.Connecting;
                    entry.LastWeight = null;
                    entry.LastError = null;
                }

                PublishIfChanged(entry, before);
                count++;
            }

            return count;
        }
    }

    /// <summary>
    /// Finds the agent connection that serves a device and the commands the device supports. Returns false when no
    /// connected agent serves it.
    /// </summary>
    public bool TryGetAgent(string deviceId, out string connectionId, out IReadOnlyList<string> supportedCommands)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        lock (_lock)
        {
            if (_devices.TryGetValue(deviceId, out var entry) && entry.ConnectionId is { } id)
            {
                connectionId = id;
                supportedCommands = entry.SupportedCommands;
                return true;
            }
        }

        connectionId = string.Empty;
        supportedCommands = [];
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<LiveDevice> GetDevices()
    {
        lock (_lock)
        {
            return [.. _devices.Values.Where(e => e.Listed).OrderBy(e => e.DeviceId, StringComparer.Ordinal).Select(e => e.ToSnapshot())];
        }
    }

    /// <inheritdoc />
    public LiveDevice? GetDevice(string deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        lock (_lock)
        {
            return _devices.TryGetValue(deviceId, out var entry) && entry.Listed ? entry.ToSnapshot() : null;
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

    /// <summary>
    /// The name of another connected agent that serves the device, or null when the device is free or served by an
    /// agent named <paramref name="agentName"/>. Call under the lock.
    /// </summary>
    private string? HeldByOtherAgent(string deviceId, string agentName) =>
        _devices.TryGetValue(deviceId, out var entry)
            && entry.ConnectionId is { } holder
            && _agentNames.TryGetValue(holder, out var holderName)
            && !string.Equals(holderName, agentName, StringComparison.OrdinalIgnoreCase)
                ? holderName
                : null;

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

        public IReadOnlyList<string> SupportedCommands { get; set; } = [];

        /// <summary>False while the entry exists only because someone watches it.</summary>
        public bool Known { get; set; }

        /// <summary>The details from the device settings; null when the device has none.</summary>
        public ConfiguredDevice? Config { get; set; }

        /// <summary>Shown in the device list: reported by an agent or present in the device settings.</summary>
        public bool Listed => Known || Config is not null;

        public List<Channel<LiveWeight>> Watchers { get; } = [];

        public List<Channel<LiveDevice>> DeviceWatchers { get; } = [];

        public LiveDevice ToSnapshot() => new(
            DeviceId, AgentName, State, LastError, LastWeight, LastSeen, SupportedCommands, Config?.Name, Config?.Capacity, Config?.Division, Config?.Unit);
    }
}

/// <summary>The details of a device from the device settings, shown with its live state.</summary>
/// <param name="Id">The device id.</param>
/// <param name="Name">The device name.</param>
/// <param name="Capacity">Maximum load in <paramref name="Unit"/>; null when unknown.</param>
/// <param name="Division">Scale interval in <paramref name="Unit"/>; null when unknown.</param>
/// <param name="Unit">Unit of capacity and division; null when unknown.</param>
public sealed record ConfiguredDevice(string Id, string Name, decimal? Capacity, decimal? Division, string? Unit);
