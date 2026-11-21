using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vyxum.Agent.Configuration;
using Vyxum.Agent.Contract;
using Vyxum.Devices;

namespace Vyxum.Agent;

/// <summary>
/// One change for the sender: a reading or a status of a device. An update without device is only a wake-up call.
/// </summary>
/// <param name="DeviceId">The device; null for a wake-up call.</param>
/// <param name="Reading">A new reading, or null.</param>
/// <param name="Status">A new status, or null.</param>
public sealed record DeviceUpdate(string? DeviceId, WeightReading? Reading, DeviceStatusInfo? Status);

/// <summary>
/// The devices of the agent with one <see cref="DeviceRunner"/> each, the latest reading of every device, and the queue
/// of changes for the sender. The queue is bounded and drops the oldest change, so reading a device never waits for
/// the network; the sender recovers dropped changes from the latest state.
/// </summary>
/// <remarks>
/// <para>The devices come from the agent's settings file when it lists any (<see cref="UsesLocalDevices"/>); they are
/// created once and never change. Otherwise they come from the web application (decision V-39) and
/// <see cref="ApplyServerDevicesAsync"/> reconciles them while the agent runs: removed and changed devices are stopped
/// (their transport, for example a serial port, is closed) before new and changed ones start; unchanged devices keep
/// running untouched. A device whose settings are invalid does not run and reports <see cref="DeviceStatus.Error"/>.</para>
/// <para>Thread safety: the device set is an immutable snapshot replaced in one step, so readers (the sender, the zero
/// command) never lock and never see a half-applied change. Changes of the set are serialized.</para>
/// </remarks>
public sealed partial class DeviceFleet
{
    private const int QueueCapacity = 256;

    private readonly Dictionary<string, WeightReading> _latest = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<DeviceUpdate> _updates;
    private readonly ILogger<DeviceFleet> _logger;
    private readonly TimeProvider _time;
    private readonly DeviceRunnerOptions _runnerOptions;
    private volatile FleetState _state = FleetState.Empty;
    private bool _running;
    private bool _stopped;

    /// <summary>
    /// Reads the settings and creates the devices of the settings file (none are started yet). Throws
    /// <see cref="AgentConfigurationException"/> when the settings are invalid.
    /// </summary>
    public DeviceFleet(IOptions<AgentSettings> options, ILogger<DeviceFleet> logger, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _logger = logger;
        _time = timeProvider;

        // Binding errors (a value of the wrong type) surface here as AgentConfigurationException.
        Settings = options.Value;
        UsesLocalDevices = Settings.Devices.Count > 0;

        var devices = AgentConfiguration.CreateDevices(Settings, timeProvider);
        _updates = Channel.CreateBounded<DeviceUpdate>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _runnerOptions = new DeviceRunnerOptions
        {
            InitialReconnectDelay = Settings.ReconnectDelay,
            MaxReconnectDelay = Settings.MaxReconnectDelay,
        };

        _state = new FleetState([.. devices.Select(d => CreateEntry(d, fingerprint: null))], new Dictionary<string, DeviceStatusInfo>());
    }

    /// <summary>The validated settings.</summary>
    public AgentSettings Settings { get; }

    /// <summary>
    /// True when the settings file lists devices. Then they are used and the devices configured in the web application
    /// are ignored.
    /// </summary>
    public bool UsesLocalDevices { get; }

    /// <summary>The devices that run now, in configuration order.</summary>
    public IReadOnlyList<AgentDevice> Devices => _state.Devices;

    /// <summary>
    /// Every device id the agent reports to the web application: the running devices and, after them, the devices
    /// whose settings are invalid.
    /// </summary>
    public IReadOnlyList<string> DeviceIds => _state.DeviceIds;

    /// <summary>The changes for the sender.</summary>
    public ChannelReader<DeviceUpdate> Updates => _updates.Reader;

    /// <summary>The runner of a running device. Throws <see cref="KeyNotFoundException"/> for any other id.</summary>
    public DeviceRunner GetRunner(string deviceId) => _state.Entries[deviceId].Runner;

    /// <summary>
    /// The current status of a device: the runner's status, <see cref="DeviceStatus.Error"/> for a device whose settings
    /// are invalid, or null for an id the agent does not serve.
    /// </summary>
    public DeviceStatusInfo? GetStatus(string deviceId)
    {
        var state = _state;
        return state.Entries.TryGetValue(deviceId, out var entry) ? entry.Runner.Status : state.Errors.GetValueOrDefault(deviceId);
    }

    /// <summary>The latest reading of a device, or null when none has arrived since it started.</summary>
    public WeightReading? GetLatestReading(string deviceId)
    {
        lock (_lock)
        {
            return _latest.GetValueOrDefault(deviceId);
        }
    }

    /// <summary>The commands a device supports (see <see cref="AgentDeviceCommands"/>).</summary>
    public IReadOnlyList<string> GetSupportedCommands(string deviceId) =>
        _state.Entries.TryGetValue(deviceId, out var entry) && entry.Device.Protocol is ISupportsZero ? [AgentDeviceCommands.Zero] : [];

    /// <summary>
    /// The serial ports of the running devices, for example <c>COM3</c>. Device discovery must not open them.
    /// </summary>
    public IReadOnlyList<string> GetPortsInUse() =>
        [.. _state.Entries.Values.Select(e => e.PortName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Sends the zero command to a device at most once and returns the outcome. A device whose protocol cannot zero
    /// answers <see cref="AgentCommandOutcome.NotSupported"/>; when <paramref name="timeout"/> passes before the command
    /// was sent, it is not sent and the result is <see cref="AgentCommandOutcome.Unavailable"/>.
    /// </summary>
    public async Task<AgentCommandResult> ZeroAsync(string deviceId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        AgentCommandResult result;
        if (deviceId is null || !_state.Entries.TryGetValue(deviceId, out var entry))
        {
            result = new AgentCommandResult(AgentCommandOutcome.Unavailable, $"The agent does not serve device '{deviceId}'.");
        }
        else if (entry.Device.Protocol is not ISupportsZero zero)
        {
            result = new AgentCommandResult(AgentCommandOutcome.NotSupported, "The device protocol does not support zeroing.");
        }
        else
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout > TimeSpan.Zero ? timeout : TimeSpan.FromMilliseconds(1));
            try
            {
                var outcome = await zero.ZeroAsync(timeoutCts.Token).ConfigureAwait(false);
                result = new AgentCommandResult(
                    outcome.Outcome switch
                    {
                        ZeroOutcome.Done => AgentCommandOutcome.Done,
                        ZeroOutcome.Rejected => AgentCommandOutcome.Rejected,
                        _ => AgentCommandOutcome.Unavailable,
                    },
                    outcome.Detail);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new AgentCommandResult(AgentCommandOutcome.Unavailable, $"The device did not finish zeroing within {timeout.TotalSeconds:0.###} s.");
            }
        }

        LogZero(_logger, deviceId ?? string.Empty, result.Result, result.Detail);
        return result;
    }

    /// <summary>Wakes the sender without a change.</summary>
    public void Wake() => _updates.Writer.TryWrite(new DeviceUpdate(null, null, null));

    /// <summary>
    /// Starts the devices, runs until <paramref name="cancellationToken"/> is cancelled, then stops every device and
    /// closes its transport. Devices applied meanwhile start at once.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!_stopped)
            {
                _running = true;
                foreach (var entry in _state.Entries.Values)
                {
                    Start(entry);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _running = false;
            _stopped = true;
            await Task.WhenAll(_state.Entries.Values.Select(StopAsync)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Replaces the devices with those sent by the web application (hub method <c>GetDevices</c>) and returns whether
    /// anything changed. Ignored when the settings file lists devices (<see cref="UsesLocalDevices"/>).
    /// </summary>
    public async Task<bool> ApplyServerDevicesAsync(IReadOnlyList<AgentDeviceConfig> configs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configs);
        if (UsesLocalDevices)
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopped)
            {
                return false;
            }

            var old = _state;
            var entries = new List<DeviceEntry>();
            var errors = new Dictionary<string, DeviceStatusInfo>(StringComparer.Ordinal);
            var errorFingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
            var added = new List<DeviceEntry>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var config in configs)
            {
                var id = config?.Id?.Trim();
                if (config is null || string.IsNullOrEmpty(id) || !ids.Add(id))
                {
                    // Not reportable without a unique id; the web application does not send such devices.
                    LogInvalidServerDevice(_logger, id ?? string.Empty, "The device id is missing or used twice.");
                    continue;
                }

                var fingerprint = config.Transport.GetRawText() + "\n" + config.Protocol.GetRawText();
                if (old.Entries.TryGetValue(id, out var running) && running.Fingerprint == fingerprint)
                {
                    entries.Add(running);
                    continue;
                }

                if (old.Errors.TryGetValue(id, out var error) && old.ErrorFingerprints.GetValueOrDefault(id) == fingerprint)
                {
                    errors[id] = error;
                    errorFingerprints[id] = fingerprint;
                    continue;
                }

                var path = $"Device {id}";
                var messages = new List<string>();
                var settings = AgentConfiguration.ReadServerDevice(config with { Id = id }, path, messages);
                var device = settings is null ? null : AgentConfiguration.CreateDevice(settings, path, messages, _time);
                if (device is null)
                {
                    var message = string.Join(" ", messages);
                    LogInvalidServerDevice(_logger, id, message);
                    errors[id] = new DeviceStatusInfo(id, DeviceStatus.Error, message, null, _time.GetUtcNow());
                    errorFingerprints[id] = fingerprint;
                    continue;
                }

                var entry = CreateEntry(device, fingerprint);
                entries.Add(entry);
                added.Add(entry);
            }

            var kept = entries.Except(added).ToHashSet();
            var removed = old.Entries.Values.Where(e => !kept.Contains(e)).ToList();
            var changed = added.Count > 0 || removed.Count > 0
                || !errors.Keys.Order(StringComparer.Ordinal).SequenceEqual(old.Errors.Keys.Order(StringComparer.Ordinal))
                || errors.Any(e => !ReferenceEquals(e.Value, old.Errors[e.Key]));
            if (!changed)
            {
                return false;
            }

            // Stop first, so that a changed device releases its serial port before it opens again.
            await Task.WhenAll(removed.Select(StopAsync)).ConfigureAwait(false);
            lock (_lock)
            {
                foreach (var entry in removed)
                {
                    _latest.Remove(entry.Device.Id);
                }
            }

            _state = new FleetState(entries, errors, errorFingerprints);
            if (_running)
            {
                foreach (var entry in added)
                {
                    Start(entry);
                }
            }

            LogApplied(_logger, added.Count, removed.Count, kept.Count, errors.Count);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Records a reading of a device and queues it for the sender.</summary>
    internal void OnReading(string deviceId, WeightReading reading)
    {
        lock (_lock)
        {
            _latest[deviceId] = reading;
        }

        _updates.Writer.TryWrite(new DeviceUpdate(deviceId, reading, null));
    }

    private DeviceEntry CreateEntry(AgentDevice device, string? fingerprint)
    {
        var runner = new DeviceRunner(device.Id, device.CreateTransport, device.Protocol, _runnerOptions, _time);
        var entry = new DeviceEntry(device, runner, fingerprint);
        runner.StatusChanged += (_, status) =>
        {
            // A device that was removed or replaced reports its last status after it left the fleet.
            if (IsCurrent(entry))
            {
                OnStatusChanged(status);
            }
        };
        return entry;
    }

    private bool IsCurrent(DeviceEntry entry) =>
        _state.Entries.TryGetValue(entry.Device.Id, out var current) && ReferenceEquals(current, entry);

    private void Start(DeviceEntry entry)
    {
        if (entry.RunTask is not null)
        {
            return;
        }

        var device = entry.Device;
        LogStarting(_logger, device.Id, device.TransportDescription, device.ProtocolName);

        // A transport may open synchronously (serial port), so each runner gets its own thread to start on.
        var token = entry.Stop.Token;
        entry.RunTask = Task.Run(() => entry.Runner.RunAsync(token), CancellationToken.None);
        entry.PumpTask = Task.Run(() => PumpAsync(entry), CancellationToken.None);
    }

    private async Task PumpAsync(DeviceEntry entry)
    {
        // Ends when the runner stops and completes its reading channel.
        await foreach (var reading in entry.Runner.Readings.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (IsCurrent(entry))
            {
                OnReading(entry.Device.Id, reading);
            }
        }
    }

    private async Task StopAsync(DeviceEntry entry)
    {
        await entry.Stop.CancelAsync().ConfigureAwait(false);
        if (entry.RunTask is { } run)
        {
            // The runner closes its transport before it returns.
            await run.ConfigureAwait(false);
            await entry.PumpTask!.ConfigureAwait(false);
            LogStopped(_logger, entry.Device.Id);
        }

        entry.Stop.Dispose();
    }

    private void OnStatusChanged(DeviceStatusInfo status)
    {
        switch (status.Status)
        {
            case DeviceStatus.Connected:
                LogDeviceConnected(_logger, status.DeviceId);
                break;
            case DeviceStatus.Disconnected when status.LastError is not null:
                LogDeviceDisconnected(_logger, status.DeviceId, status.LastError, status.RetryIn ?? TimeSpan.Zero);
                break;
            default:
                LogDeviceStatus(_logger, status.DeviceId, status.Status);
                break;
        }

        _updates.Writer.TryWrite(new DeviceUpdate(status.DeviceId, null, status));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting device {DeviceId}: {Transport}, protocol {Protocol}.")]
    private static partial void LogStarting(ILogger logger, string deviceId, string transport, string protocol);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device {DeviceId} stopped.")]
    private static partial void LogStopped(ILogger logger, string deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Devices from the web application applied: {Started} started, {Stopped} stopped, {Unchanged} unchanged, {Invalid} invalid.")]
    private static partial void LogApplied(ILogger logger, int started, int stopped, int unchanged, int invalid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device {DeviceId} from the web application is not started: {Error}")]
    private static partial void LogInvalidServerDevice(ILogger logger, string deviceId, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device {DeviceId} is connected.")]
    private static partial void LogDeviceConnected(ILogger logger, string deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device {DeviceId} is disconnected: {Error} Retrying in {RetryIn}.")]
    private static partial void LogDeviceDisconnected(ILogger logger, string deviceId, string error, TimeSpan retryIn);

    [LoggerMessage(Level = LogLevel.Information, Message = "Zero requested for device {DeviceId}: {Result}. {Detail}")]
    private static partial void LogZero(ILogger logger, string deviceId, AgentCommandOutcome result, string? detail);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Device {DeviceId} is {Status}.")]
    private static partial void LogDeviceStatus(ILogger logger, string deviceId, DeviceStatus status);

    /// <summary>A device with its runner. <see cref="Fingerprint"/> is the settings text it was created from (web devices only).</summary>
    private sealed class DeviceEntry(AgentDevice device, DeviceRunner runner, string? fingerprint)
    {
        public AgentDevice Device { get; } = device;

        public DeviceRunner Runner { get; } = runner;

        public string? Fingerprint { get; } = fingerprint;

        /// <summary>The serial port the device uses; null for other transports.</summary>
        public string? PortName { get; } = device.PortName;

        public CancellationTokenSource Stop { get; } = new();

        public Task? RunTask { get; set; }

        public Task? PumpTask { get; set; }
    }

    /// <summary>An immutable view of the device set.</summary>
    private sealed class FleetState
    {
        public static readonly FleetState Empty = new([], new Dictionary<string, DeviceStatusInfo>());

        public FleetState(
            IReadOnlyList<DeviceEntry> entries,
            IReadOnlyDictionary<string, DeviceStatusInfo> errors,
            IReadOnlyDictionary<string, string>? errorFingerprints = null)
        {
            Entries = entries.ToDictionary(e => e.Device.Id, StringComparer.Ordinal);
            Devices = [.. entries.Select(e => e.Device)];
            Errors = errors;
            ErrorFingerprints = errorFingerprints ?? new Dictionary<string, string>(StringComparer.Ordinal);
            DeviceIds = [.. Devices.Select(d => d.Id), .. errors.Keys.Order(StringComparer.Ordinal)];
        }

        public IReadOnlyDictionary<string, DeviceEntry> Entries { get; }

        public IReadOnlyList<AgentDevice> Devices { get; }

        public IReadOnlyList<string> DeviceIds { get; }

        public IReadOnlyDictionary<string, DeviceStatusInfo> Errors { get; }

        public IReadOnlyDictionary<string, string> ErrorFingerprints { get; }
    }
}
