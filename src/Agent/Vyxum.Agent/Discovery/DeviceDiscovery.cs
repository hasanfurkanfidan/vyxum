using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vyxum.Agent.Configuration;
using Vyxum.Agent.Contract;
using Vyxum.Devices;
using Vyxum.Devices.Discovery;
using Vyxum.Devices.Transports;

namespace Vyxum.Agent.Discovery;

/// <summary>
/// Carries out <see cref="AgentHubContract.Discover"/>: scans the serial ports that no running device uses (or one network
/// address) with <see cref="PortScanner"/> and turns what it finds into device settings in the shape of
/// <see cref="TransportSettings"/> and <see cref="ProtocolSettings"/>, so they can be saved as they are. Only read-only
/// commands reach a device (decision V-40). One discovery runs at a time.
/// </summary>
public sealed partial class DeviceDiscovery
{
    /// <summary>The longest a discovery may take on the agent; the web application waits a little longer.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(50);

    private static readonly JsonSerializerOptions TransportJson = CreateJson(JsonIgnoreCondition.WhenWritingNull);
    private static readonly JsonSerializerOptions ProtocolJson = CreateJson(JsonIgnoreCondition.WhenWritingDefault);
    private static readonly string[] SerialOnlyKeys = ["portName", "baudRate", "dataBits", "parity", "stopBits", "handshake", "dtrEnable", "rtsEnable"];
    private static readonly string[] TcpOnlyKeys = ["host", "port", "connectTimeout", "keepAlive"];

    private readonly DeviceFleet _fleet;
    private readonly PortScannerOptions _scannerOptions;
    private readonly TimeProvider _time;
    private readonly ILogger<DeviceDiscovery> _logger;
    private readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>Creates the service.</summary>
    public DeviceDiscovery(DeviceFleet fleet, IOptions<PortScannerOptions> scannerOptions, TimeProvider timeProvider, ILogger<DeviceDiscovery> logger)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(scannerOptions);
        _fleet = fleet;
        _scannerOptions = scannerOptions.Value;
        _time = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Scans and returns the ports and the devices found. <paramref name="reportProgress"/> is called with the port states
    /// while scanning (the latest state wins when sending is slower than scanning); its failures are ignored.
    /// Throws <see cref="InvalidOperationException"/> when a discovery is already running.
    /// </summary>
    public async Task<DiscoveryResult> DiscoverAsync(
        DiscoveryRequest request,
        Func<IReadOnlyList<DiscoveryPortState>, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await _running.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A device discovery is already running on this agent.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(MaxDuration);
            var tcp = request.Tcp is { } target
                ? new TcpTransportOptions { Host = target.Host?.Trim() ?? string.Empty, Port = target.Port }
                : null;
            if (tcp is not null && (string.IsNullOrEmpty(tcp.Host) || tcp.Port is < 1 or > 65535))
            {
                throw new ArgumentException("The network address needs a host and a port between 1 and 65535.", nameof(request));
            }

            var portsInUse = _fleet.GetPortUsers();
            LogStarted(_logger, request.DiscoveryId, tcp is null ? "serial ports" : $"{tcp.Host}:{tcp.Port}", portsInUse.Count);

            var updates = Channel.CreateBounded<IReadOnlyList<DiscoveryPortState>>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
            var pump = reportProgress is null ? Task.CompletedTask : PumpAsync(updates.Reader, reportProgress);
            var start = _time.GetTimestamp();
            PortScanReport report;
            try
            {
                var scanner = new PortScanner(_scannerOptions, _time);
                report = await scanner.ScanAsync(
                    new PortScanRequest(portsInUse, tcp),
                    new InlineProgress(ports => updates.Writer.TryWrite([.. ports.Select(ToContract)])),
                    timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                updates.Writer.TryComplete();
                await pump.ConfigureAwait(false);
            }

            var candidates = new List<DiscoveryCandidate>();
            foreach (var device in report.Devices)
            {
                if (ToCandidate(device, _time) is { } candidate)
                {
                    candidates.Add(candidate);
                }
                else
                {
                    LogUnusable(_logger, device.Description);
                }
            }

            LogFinished(_logger, request.DiscoveryId, candidates.Count, report.Ports.Count, _time.GetElapsedTime(start));
            return new DiscoveryResult([.. report.Ports.Select(ToContract)], candidates);
        }
        finally
        {
            _running.Release();
        }
    }

    /// <summary>
    /// Turns a found device into a candidate whose settings the agent itself can run; null when they cannot be run (a
    /// probe proposed settings the agent does not accept).
    /// </summary>
    public static DiscoveryCandidate? ToCandidate(DiscoveredDevice device, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        var transport = ToTransportSettings(device);
        var protocol = ToProtocolSettings(device.Match);
        if (transport is null || protocol is null)
        {
            return null;
        }

        var errors = new List<string>();
        if (AgentConfiguration.CreateDevice(new DeviceSettings { Id = "discovered", Transport = transport, Protocol = protocol }, "Discovered", errors, timeProvider) is null)
        {
            return null;
        }

        var transportJson = JsonSerializer.SerializeToNode(transport, TransportJson)!.AsObject();
        foreach (var name in device.Tcp is null ? TcpOnlyKeys : SerialOnlyKeys)
        {
            transportJson.Remove(name);
        }

        var reading = device.Match.Reading is { } r
            ? new DiscoveryReading(r.Value?.ToString(CultureInfo.InvariantCulture), r.Unit, r.State)
            : null;
        var key = device.Tcp is { } tcp ? $"tcp:{tcp.Host}:{tcp.Port}" : $"serial:{device.PortName}";
        return new DiscoveryCandidate(
            key,
            device.PortName,
            device.Tcp?.Host,
            device.Tcp?.Port,
            ToElement(transportJson),
            JsonSerializer.SerializeToElement(protocol, ProtocolJson),
            device.Match.SerialNumber,
            reading,
            device.Description);
    }

    private static TransportSettings? ToTransportSettings(DiscoveredDevice device)
    {
        if (device.Serial is { } serial)
        {
            return new TransportSettings
            {
                Type = "serial",
                PortName = serial.PortName,
                BaudRate = serial.BaudRate,
                DataBits = serial.DataBits,
                Parity = serial.Parity,
                StopBits = serial.StopBits,
                Handshake = serial.Handshake,
                DtrEnable = serial.DtrEnable,
                RtsEnable = serial.RtsEnable,
            };
        }

        return device.Tcp is { } tcp ? new TransportSettings { Type = "tcp", Host = tcp.Host, Port = tcp.Port } : null;
    }

    private static ProtocolSettings? ToProtocolSettings(ProbeMatch match)
    {
        var json = new JsonObject { ["type"] = match.ProtocolType };
        foreach (var (key, value) in match.ProtocolSettings)
        {
            json[key] = JsonSerializer.SerializeToNode(value, value.GetType());
        }

        try
        {
            return json.Deserialize<ProtocolSettings>(ProtocolJson);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static DiscoveryPortState ToContract(PortScanStatus status) => new(
        status.PortName,
        status.State switch
        {
            PortScanState.Pending => DiscoveryPortStatus.Pending,
            PortScanState.Scanning => DiscoveryPortStatus.Scanning,
            PortScanState.Found => DiscoveryPortStatus.Found,
            PortScanState.NotFound => DiscoveryPortStatus.NotFound,
            PortScanState.InUse => DiscoveryPortStatus.InUse,
            _ => DiscoveryPortStatus.Error,
        },
        status.Detail);

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private async Task PumpAsync(ChannelReader<IReadOnlyList<DiscoveryPortState>> reader, Func<IReadOnlyList<DiscoveryPortState>, Task> send)
    {
        await foreach (var ports in reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await send(ports).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Progress is a convenience; the result is what counts.
                LogProgressFailed(_logger, ex.Message);
            }
        }
    }

    private static JsonSerializerOptions CreateJson(JsonIgnoreCondition ignore)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = ignore };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Device discovery {DiscoveryId} started on {Target}; {InUse} serial port(s) are used by running devices and are skipped.")]
    private static partial void LogStarted(ILogger logger, string discoveryId, string target, int inUse);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device discovery {DiscoveryId} found {Count} device(s) on {Ports} port(s) in {Elapsed}.")]
    private static partial void LogFinished(ILogger logger, string discoveryId, int count, int ports, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A device was found ({Description}) but its settings cannot be run by this agent; it is not offered.")]
    private static partial void LogUnusable(ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sending discovery progress failed: {Error}")]
    private static partial void LogProgressFailed(ILogger logger, string error);

    private sealed class InlineProgress(Action<IReadOnlyList<PortScanStatus>> report) : IProgress<IReadOnlyList<PortScanStatus>>
    {
        public void Report(IReadOnlyList<PortScanStatus> value) => report(value);
    }
}
