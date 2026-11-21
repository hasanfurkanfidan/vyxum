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
/// The configured devices with one <see cref="DeviceRunner"/> each, the latest reading of every device, and the queue
/// of changes for the sender. The queue is bounded and drops the oldest change, so reading a device never waits for
/// the network; the sender recovers dropped changes from the latest state.
/// </summary>
public sealed partial class DeviceFleet
{
    private const int QueueCapacity = 256;

    private readonly Dictionary<string, DeviceRunner> _runners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WeightReading> _latest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IScaleProtocol> _protocols = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly Channel<DeviceUpdate> _updates;
    private readonly ILogger<DeviceFleet> _logger;

    /// <summary>Reads the settings and creates the devices. Throws <see cref="AgentConfigurationException"/> when they are invalid.</summary>
    public DeviceFleet(IOptions<AgentSettings> options, ILogger<DeviceFleet> logger, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _logger = logger;

        // Binding errors (a value of the wrong type) surface here as AgentConfigurationException.
        Settings = options.Value;

        Devices = AgentConfiguration.CreateDevices(Settings, timeProvider);
        _updates = Channel.CreateBounded<DeviceUpdate>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        var runnerOptions = new DeviceRunnerOptions
        {
            InitialReconnectDelay = Settings.ReconnectDelay,
            MaxReconnectDelay = Settings.MaxReconnectDelay,
        };
        foreach (var device in Devices)
        {
            var runner = new DeviceRunner(device.Id, device.CreateTransport, device.Protocol, runnerOptions, timeProvider);
            runner.StatusChanged += OnStatusChanged;
            _runners.Add(device.Id, runner);
            _protocols.Add(device.Id, device.Protocol);
        }
    }

    /// <summary>The validated settings.</summary>
    public AgentSettings Settings { get; }

    /// <summary>The configured devices, in configuration order.</summary>
    public IReadOnlyList<AgentDevice> Devices { get; }

    /// <summary>The changes for the sender.</summary>
    public ChannelReader<DeviceUpdate> Updates => _updates.Reader;

    /// <summary>The runner of a device.</summary>
    public DeviceRunner GetRunner(string deviceId) => _runners[deviceId];

    /// <summary>The current status of a device.</summary>
    public DeviceStatusInfo GetStatus(string deviceId) => _runners[deviceId].Status;

    /// <summary>The latest reading of a device, or null when none has arrived since the agent started.</summary>
    public WeightReading? GetLatestReading(string deviceId)
    {
        lock (_lock)
        {
            return _latest.GetValueOrDefault(deviceId);
        }
    }

    /// <summary>The commands a device supports (see <see cref="AgentDeviceCommands"/>).</summary>
    public IReadOnlyList<string> GetSupportedCommands(string deviceId) =>
        _protocols.TryGetValue(deviceId, out var protocol) && protocol is ISupportsZero ? [AgentDeviceCommands.Zero] : [];

    /// <summary>
    /// Sends the zero command to a device at most once and returns the outcome. A device whose protocol cannot zero
    /// answers <see cref="AgentCommandOutcome.NotSupported"/>; when <paramref name="timeout"/> passes before the command
    /// was sent, it is not sent and the result is <see cref="AgentCommandOutcome.Unavailable"/>.
    /// </summary>
    public async Task<AgentCommandResult> ZeroAsync(string deviceId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        AgentCommandResult result;
        if (deviceId is null || !_protocols.TryGetValue(deviceId, out var protocol))
        {
            result = new AgentCommandResult(AgentCommandOutcome.Unavailable, $"The agent does not serve device '{deviceId}'.");
        }
        else if (protocol is not ISupportsZero zero)
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

    /// <summary>Records a reading of a device and queues it for the sender.</summary>
    internal void OnReading(string deviceId, WeightReading reading)
    {
        lock (_lock)
        {
            _latest[deviceId] = reading;
        }

        _updates.Writer.TryWrite(new DeviceUpdate(deviceId, reading, null));
    }

    private void OnStatusChanged(object? sender, DeviceStatusInfo status)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Device {DeviceId} is connected.")]
    private static partial void LogDeviceConnected(ILogger logger, string deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device {DeviceId} is disconnected: {Error} Retrying in {RetryIn}.")]
    private static partial void LogDeviceDisconnected(ILogger logger, string deviceId, string error, TimeSpan retryIn);

    [LoggerMessage(Level = LogLevel.Information, Message = "Zero requested for device {DeviceId}: {Result}. {Detail}")]
    private static partial void LogZero(ILogger logger, string deviceId, AgentCommandOutcome result, string? detail);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Device {DeviceId} is {Status}.")]
    private static partial void LogDeviceStatus(ILogger logger, string deviceId, DeviceStatus status);
}
