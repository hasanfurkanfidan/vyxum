using System.Text.Json;
using Vyxum.Devices;

namespace Vyxum.Agent.Contract;

/// <summary>
/// The wire contract with the agent hub of the Vyxum web application. The agent does not reference the web
/// application, so these types mirror the platform's contract; a test keeps the two in step. Enums travel as strings
/// and property names are camel case.
/// </summary>
public static class AgentHubContract
{
    /// <summary>The hub path.</summary>
    public const string Path = "/hubs/agent";

    /// <summary>Header that carries the agent key.</summary>
    public const string KeyHeader = "X-Vyxum-Agent-Key";

    /// <summary>Hub method: <c>Register(AgentRegistration)</c>.</summary>
    public const string Register = "Register";

    /// <summary>Hub method: <c>ReportReading(string deviceId, AgentReading reading)</c>.</summary>
    public const string ReportReading = "ReportReading";

    /// <summary>Hub method: <c>ReportStatus(string deviceId, AgentDeviceStatus status)</c>.</summary>
    public const string ReportStatus = "ReportStatus";

    /// <summary>
    /// Client method called by the hub with a result: <c>AgentCommandResult Zero(string deviceId, TimeSpan timeout)</c>.
    /// The agent sends the zero command to the device at most once and answers within <c>timeout</c>.
    /// </summary>
    public const string Zero = "Zero";

    /// <summary>
    /// Hub method: <c>AgentDeviceConfig[] GetDevices()</c>, called after <see cref="Register"/>. Returns the devices from
    /// the device settings of the web application that this agent serves.
    /// </summary>
    public const string GetDevices = "GetDevices";

    /// <summary>Client method without arguments: <c>DevicesChanged()</c>. The device settings changed; call <see cref="GetDevices"/> again.</summary>
    public const string DevicesChanged = "DevicesChanged";

    /// <summary>
    /// Client method called by the hub with a result: <c>DiscoveryResult Discover(DiscoveryRequest request)</c>. The agent
    /// looks for scales on its free serial ports (or at one network address) with read-only commands (decision V-40) and
    /// answers with what it found. While it scans it sends <see cref="ReportDiscoveryProgress"/>.
    /// </summary>
    public const string Discover = "Discover";

    /// <summary>Hub method: <c>ReportDiscoveryProgress(string discoveryId, DiscoveryPortState[] ports)</c>, sent while scanning.</summary>
    public const string ReportDiscoveryProgress = "ReportDiscoveryProgress";

    /// <summary>
    /// Client method invoked with a result: <c>AgentPrintResult PrintRaw(AgentPrintRequest request)</c>. The agent sends
    /// the bytes to the printer at most once and answers within <c>request.Timeout</c>.
    /// </summary>
    public const string PrintRaw = "PrintRaw";
}

/// <summary>Names of the features listed in <see cref="AgentRegistration.Features"/>.</summary>
public static class AgentFeatures
{
    /// <summary>The agent carries out <see cref="AgentHubContract.PrintRaw"/>.</summary>
    public const string RawPrint = "RawPrint";
}

/// <summary>How a printer is connected.</summary>
public enum AgentPrinterConnection
{
    /// <summary>Over TCP.</summary>
    Network,

    /// <summary>On a serial port.</summary>
    Serial,
}

/// <summary>A print job from the web application.</summary>
/// <param name="Connection">How the printer is connected.</param>
/// <param name="Host">Network: host name or IP address.</param>
/// <param name="Port">Network: TCP port.</param>
/// <param name="PortName">Serial: the port name.</param>
/// <param name="BaudRate">Serial: baud rate (8N1).</param>
/// <param name="Data">The bytes to send.</param>
/// <param name="ReplyMaxBytes">When above 0, the answer is read after sending, up to this many bytes.</param>
/// <param name="ReplyTimeout">How long to wait for the answer.</param>
/// <param name="ReplyEndByte">When set, reading stops after this byte has arrived <paramref name="ReplyEndCount"/> times.</param>
/// <param name="ReplyEndCount">How many end bytes end the answer.</param>
/// <param name="Timeout">How long the whole job may take.</param>
public sealed record AgentPrintRequest(
    AgentPrinterConnection Connection,
    string? Host,
    int? Port,
    string? PortName,
    int? BaudRate,
    byte[] Data,
    int ReplyMaxBytes,
    TimeSpan ReplyTimeout,
    byte? ReplyEndByte,
    int ReplyEndCount,
    TimeSpan Timeout);

/// <summary>The outcome of a print job. The agent answers only the first four.</summary>
public enum AgentPrintOutcome
{
    /// <summary>Every byte was written to the printer connection.</summary>
    Sent,

    /// <summary>Printing is turned off on this agent, or the job is not valid; nothing was sent.</summary>
    Unavailable,

    /// <summary>The target is not allowed on this agent; nothing was sent.</summary>
    Refused,

    /// <summary>The printer could not be reached or the connection broke.</summary>
    Failed,

    /// <summary>Used by the web application only.</summary>
    Uncertain,
}

/// <summary>The answer to <see cref="AgentHubContract.PrintRaw"/>.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Reply">The printer's answer when one was asked for.</param>
/// <param name="Detail">A short English explanation for logs.</param>
public sealed record AgentPrintResult(AgentPrintOutcome Outcome, byte[]? Reply = null, string? Detail = null);

/// <summary>What <see cref="AgentHubContract.Discover"/> should scan.</summary>
/// <param name="DiscoveryId">The id of the discovery job in the web application; progress reports carry it.</param>
/// <param name="Tcp">When set, only this network address is probed; otherwise every free serial port.</param>
public sealed record DiscoveryRequest(string DiscoveryId, DiscoveryTcpTarget? Tcp = null);

/// <summary>A network address to probe.</summary>
/// <param name="Host">Host name or IP address.</param>
/// <param name="Port">TCP port.</param>
public sealed record DiscoveryTcpTarget(string Host, int Port);

/// <summary>The state of one port during discovery.</summary>
public enum DiscoveryPortStatus
{
    /// <summary>Waiting to be scanned.</summary>
    Pending,

    /// <summary>Being scanned.</summary>
    Scanning,

    /// <summary>A device was found.</summary>
    Found,

    /// <summary>No device answered.</summary>
    NotFound,

    /// <summary>Used by a running device or another program; not touched.</summary>
    InUse,

    /// <summary>The port could not be opened or failed.</summary>
    Error,
}

/// <summary>One port and its state.</summary>
/// <param name="PortName">The serial port name, or <c>host:port</c> for a network probe.</param>
/// <param name="State">The state.</param>
/// <param name="Detail">
/// For a port used by a running device of the agent, the id of that device; for other ports in use and errors, a short
/// English explanation; null otherwise.
/// </param>
public sealed record DiscoveryPortState(string PortName, DiscoveryPortStatus State, string? Detail);

/// <summary>A weight read while probing.</summary>
/// <param name="Value">The weight exactly as transmitted (for example <c>"572"</c> or <c>"1.250"</c>); null for overload and underload.</param>
/// <param name="Unit">The unit; null when the device sent none.</param>
/// <param name="State">Stable, motion, overload or underload.</param>
public sealed record DiscoveryReading(string? Value, string? Unit, WeightState State);

/// <summary>A device found by discovery, ready to be saved as device settings.</summary>
/// <param name="Key">A key unique within the result, for example <c>serial:COM3</c>.</param>
/// <param name="PortName">The serial port; null for a network device.</param>
/// <param name="Host">The host of a network device; null for a serial port.</param>
/// <param name="Port">The TCP port of a network device; null for a serial port.</param>
/// <param name="Transport">The transport settings: a JSON object with the shape of <see cref="Configuration.TransportSettings"/>.</param>
/// <param name="Protocol">The protocol settings: a JSON object with the shape of <see cref="Configuration.ProtocolSettings"/>.</param>
/// <param name="SerialNumber">The serial number when the device reported it; otherwise null.</param>
/// <param name="Reading">The weight read while probing; null when the device answered without one.</param>
/// <param name="Description">A short description, for example <c>MT-SICS, 9600 8N1</c>.</param>
public sealed record DiscoveryCandidate(
    string Key,
    string? PortName,
    string? Host,
    int? Port,
    JsonElement Transport,
    JsonElement Protocol,
    string? SerialNumber,
    DiscoveryReading? Reading,
    string Description);

/// <summary>The answer to <see cref="AgentHubContract.Discover"/>.</summary>
/// <param name="Ports">Every port with its final state.</param>
/// <param name="Candidates">The devices found.</param>
public sealed record DiscoveryResult(IReadOnlyList<DiscoveryPortState> Ports, IReadOnlyList<DiscoveryCandidate> Candidates);

/// <summary>One device from <see cref="AgentHubContract.GetDevices"/>.</summary>
/// <param name="Id">The device id.</param>
/// <param name="Transport">The transport settings: a JSON object with the shape of <see cref="Configuration.TransportSettings"/>.</param>
/// <param name="Protocol">The protocol settings: a JSON object with the shape of <see cref="Configuration.ProtocolSettings"/>.</param>
public sealed record AgentDeviceConfig(string Id, JsonElement Transport, JsonElement Protocol);

/// <summary>Names of the commands a device can carry out, as reported in <see cref="AgentRegistration.DeviceCommands"/>.</summary>
public static class AgentDeviceCommands
{
    /// <summary>Set the zero point of the scale (MT-SICS <c>Z</c>).</summary>
    public const string Zero = "Zero";
}

/// <summary>Sent after every (re)connect.</summary>
/// <param name="AgentName">A name for the agent, usually the computer name.</param>
/// <param name="Version">The agent version.</param>
/// <param name="DeviceIds">The devices the agent serves.</param>
/// <param name="DeviceCommands">For each device id, the commands it supports (see <see cref="AgentDeviceCommands"/>).</param>
/// <param name="Features">The features of the agent (see <see cref="AgentFeatures"/>).</param>
public sealed record AgentRegistration(
    string AgentName,
    string? Version,
    IReadOnlyList<string> DeviceIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DeviceCommands = null,
    IReadOnlyList<string>? Features = null);

/// <summary>The outcome of a device command.</summary>
public enum AgentCommandOutcome
{
    /// <summary>The device carried out the command.</summary>
    Done,

    /// <summary>The device or its protocol does not support the command.</summary>
    NotSupported,

    /// <summary>The device answered but refused the command (for example the weight is in motion).</summary>
    Rejected,

    /// <summary>The device is not connected or did not answer in time.</summary>
    Unavailable,
}

/// <summary>The answer to a device command.</summary>
/// <param name="Result">The outcome.</param>
/// <param name="Detail">A short English explanation for logs; null when there is nothing to add.</param>
public sealed record AgentCommandResult(AgentCommandOutcome Result, string? Detail = null);

/// <summary>One weight read by the agent.</summary>
/// <param name="Value">The weight as transmitted, with its scale. Null for overload and underload.</param>
/// <param name="Unit">The unit reported by the device; null when it sent none.</param>
/// <param name="State">Stable, motion, overload or underload.</param>
/// <param name="Tare">The tare weight when the device reports it; otherwise null.</param>
/// <param name="IsNet">True for net, false for gross, null when the device does not say.</param>
/// <param name="Raw">The raw frame text, for diagnostics.</param>
/// <param name="Timestamp">When the agent read the weight.</param>
public sealed record AgentReading(
    decimal? Value,
    string? Unit,
    WeightState State,
    decimal? Tare,
    bool? IsNet,
    string? Raw,
    DateTimeOffset Timestamp)
{
    /// <summary>Creates the wire form of a reading.</summary>
    public static AgentReading From(WeightReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return new AgentReading(reading.Value, reading.Unit, reading.State, reading.Tare, reading.IsNet, reading.Raw, reading.Timestamp);
    }
}

/// <summary>A device status change.</summary>
/// <param name="Status">The new status.</param>
/// <param name="LastError">The error that caused a disconnect; null otherwise.</param>
/// <param name="RetryIn">When disconnected after an error, the wait before the next attempt.</param>
/// <param name="Timestamp">When the status changed.</param>
public sealed record AgentDeviceStatus(
    DeviceStatus Status,
    string? LastError,
    TimeSpan? RetryIn,
    DateTimeOffset Timestamp)
{
    /// <summary>Creates the wire form of a status.</summary>
    public static AgentDeviceStatus From(DeviceStatusInfo status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new AgentDeviceStatus(status.Status, status.LastError, status.RetryIn, status.Timestamp);
    }
}
