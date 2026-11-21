using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vyxum.Agent.Configuration;
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Device {DeviceId} is {Status}.")]
    private static partial void LogDeviceStatus(ILogger logger, string deviceId, DeviceStatus status);
}
