using System.Text.Json;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// The wire contract between Vyxum Agent and the platform's agent hub. Enums travel as strings.
/// </summary>
public static class AgentHubContract
{
    /// <summary>The hub path.</summary>
    public const string Path = "/hubs/agent";

    /// <summary>Header that carries the agent key. It is the only place the key is accepted.</summary>
    public const string KeyHeader = "X-Vyxum-Agent-Key";

    /// <summary>Hub method: <c>Register(AgentRegistration)</c>.</summary>
    public const string Register = "Register";

    /// <summary>Hub method: <c>ReportReading(string deviceId, AgentReading reading)</c>.</summary>
    public const string ReportReading = "ReportReading";

    /// <summary>Hub method: <c>ReportStatus(string deviceId, AgentDeviceStatus status)</c>.</summary>
    public const string ReportStatus = "ReportStatus";

    /// <summary>
    /// Client method invoked with a result: <c>AgentCommandResult Zero(string deviceId, TimeSpan timeout)</c>.
    /// The agent sends the zero command to the device at most once and answers within <c>timeout</c>.
    /// </summary>
    public const string Zero = "Zero";

    /// <summary>
    /// Hub method: <c>AgentDeviceConfig[] GetDevices()</c>. Returns the enabled devices from the device settings that the
    /// calling agent serves (see <see cref="AgentDeviceAssignments"/>). Call it after <see cref="Register"/>. An agent that
    /// calls it takes part in the device settings and is sent <see cref="DevicesChanged"/>; agents that never call it
    /// (older agents, agents with devices in their own settings file) are left alone.
    /// </summary>
    public const string GetDevices = "GetDevices";

    /// <summary>
    /// Client method without arguments: <c>DevicesChanged()</c>. The device settings changed; the agent calls
    /// <see cref="GetDevices"/> again. Sent only to agents that have called <see cref="GetDevices"/>.
    /// </summary>
    public const string DevicesChanged = "DevicesChanged";

    /// <summary>
    /// Client method invoked with a result: <c>DiscoveryResult Discover(DiscoveryRequest request)</c>. The agent looks for
    /// scales on its free serial ports (or at one network address) with read-only commands (decision V-40). See
    /// <see cref="DiscoveryJobs"/>.
    /// </summary>
    public const string Discover = "Discover";

    /// <summary>Hub method: <c>ReportDiscoveryProgress(string discoveryId, DiscoveryPortState[] ports)</c>, sent while scanning.</summary>
    public const string ReportDiscoveryProgress = "ReportDiscoveryProgress";

    /// <summary>
    /// Client method invoked with a result: <c>AgentPrintResult PrintRaw(AgentPrintRequest request)</c>. The agent sends
    /// the bytes to the printer at most once and answers within <c>request.Timeout</c>. Only sent to agents that list
    /// <see cref="AgentFeatures.RawPrint"/> when they register.
    /// </summary>
    public const string PrintRaw = "PrintRaw";
}

/// <summary>Names of the features an agent lists in <see cref="AgentRegistration.Features"/>.</summary>
public static class AgentFeatures
{
    /// <summary>The agent carries out <see cref="AgentHubContract.PrintRaw"/>.</summary>
    public const string RawPrint = "RawPrint";
}

/// <summary>A print job sent to an agent with <see cref="AgentHubContract.PrintRaw"/>.</summary>
/// <param name="Connection">How the printer is connected.</param>
/// <param name="Host">Network: host name or IP address.</param>
/// <param name="Port">Network: TCP port.</param>
/// <param name="PortName">Serial: the port name.</param>
/// <param name="BaudRate">Serial: baud rate (8N1).</param>
/// <param name="Data">The bytes to send.</param>
/// <param name="ReplyMaxBytes">When above 0, the answer is read after sending, up to this many bytes.</param>
/// <param name="ReplyTimeout">How long to wait for the answer.</param>
/// <param name="ReplyEndByte">When set, reading stops after this byte.</param>
/// <param name="Timeout">How long the whole job may take on the agent.</param>
public sealed record AgentPrintRequest(
    RawPrinterConnection Connection,
    string? Host,
    int? Port,
    string? PortName,
    int? BaudRate,
    byte[] Data,
    int ReplyMaxBytes,
    TimeSpan ReplyTimeout,
    byte? ReplyEndByte,
    TimeSpan Timeout);

/// <summary>The answer of an agent to <see cref="AgentHubContract.PrintRaw"/>.</summary>
/// <param name="Outcome">The outcome. An agent answers <c>Sent</c>, <c>Unavailable</c>, <c>Refused</c> or <c>Failed</c>.</param>
/// <param name="Reply">The printer's answer when one was asked for.</param>
/// <param name="Detail">A short English explanation for logs.</param>
public sealed record AgentPrintResult(RawPrintOutcome Outcome, byte[]? Reply = null, string? Detail = null);

/// <summary>What <see cref="AgentHubContract.Discover"/> should scan.</summary>
/// <param name="DiscoveryId">The id of the discovery job; progress reports carry it.</param>
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
/// <param name="Value">The weight exactly as transmitted, as text (scale kept); null for overload and underload.</param>
/// <param name="Unit">The unit; null when the device sent none.</param>
/// <param name="State">Stable, motion, overload or underload.</param>
public sealed record DiscoveryReading(string? Value, string? Unit, LiveWeightState State);

/// <summary>A device found by discovery. Its transport and protocol objects can be saved as device settings unchanged.</summary>
/// <param name="Key">A key unique within the result.</param>
/// <param name="PortName">The serial port; null for a network device.</param>
/// <param name="Host">The host of a network device; null for a serial port.</param>
/// <param name="Port">The TCP port of a network device; null for a serial port.</param>
/// <param name="Transport">The transport settings, a JSON object with <c>type</c>.</param>
/// <param name="Protocol">The protocol settings, a JSON object with <c>type</c>.</param>
/// <param name="SerialNumber">The serial number when the device reported it; otherwise null.</param>
/// <param name="Reading">The weight read while probing; null when the device answered without one.</param>
/// <param name="Description">A short technical description (protocol and settings).</param>
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

/// <summary>The answer of an agent to <see cref="AgentHubContract.Discover"/>.</summary>
/// <param name="Ports">Every port with its final state.</param>
/// <param name="Candidates">The devices found.</param>
public sealed record DiscoveryResult(IReadOnlyList<DiscoveryPortState> Ports, IReadOnlyList<DiscoveryCandidate> Candidates);

/// <summary>One device an agent should run, from <see cref="AgentHubContract.GetDevices"/>.</summary>
/// <param name="Id">The device id.</param>
/// <param name="Transport">The transport settings, a JSON object with <c>type</c>; the agent reads the rest.</param>
/// <param name="Protocol">The protocol settings, a JSON object with <c>type</c>; the agent reads the rest.</param>
public sealed record AgentDeviceConfig(string Id, JsonElement Transport, JsonElement Protocol);

/// <summary>Sent by an agent after connecting.</summary>
/// <param name="AgentName">A name for the agent, usually the computer name.</param>
/// <param name="Version">The agent version.</param>
/// <param name="DeviceIds">The devices the agent serves.</param>
/// <param name="DeviceCommands">
/// For each device id, the commands it supports (see <see cref="DeviceCommandNames"/>); null from an agent that does
/// not report them, which means none.
/// </param>
/// <param name="Features">The features of the agent (see <see cref="AgentFeatures"/>); null from an older agent, which means none.</param>
public sealed record AgentRegistration(
    string AgentName,
    string? Version,
    IReadOnlyList<string> DeviceIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DeviceCommands = null,
    IReadOnlyList<string>? Features = null);

/// <summary>The answer of an agent to a device command.</summary>
/// <param name="Result">The outcome.</param>
/// <param name="Detail">A short English explanation for logs; null when there is nothing to add.</param>
public sealed record AgentCommandResult(DeviceCommandOutcome Result, string? Detail = null);

/// <summary>One weight read by an agent.</summary>
/// <param name="Value">The weight as transmitted. Null for overload and underload.</param>
/// <param name="Unit">The unit reported by the device; null when it sent none.</param>
/// <param name="State">Stable, motion, overload or underload.</param>
/// <param name="Tare">The tare weight when the device reports it; otherwise null.</param>
/// <param name="IsNet">True for net, false for gross, null when the device does not say.</param>
/// <param name="Raw">The raw frame text, for diagnostics.</param>
/// <param name="Timestamp">When the agent read the weight.</param>
public sealed record AgentReading(
    decimal? Value,
    string? Unit,
    LiveWeightState State,
    decimal? Tare,
    bool? IsNet,
    string? Raw,
    DateTimeOffset Timestamp);

/// <summary>A device status change reported by an agent.</summary>
/// <param name="Status">The new status.</param>
/// <param name="LastError">The error that caused a disconnect; null otherwise.</param>
/// <param name="RetryIn">When disconnected after an error, the wait before the next attempt.</param>
/// <param name="Timestamp">When the status changed.</param>
public sealed record AgentDeviceStatus(
    DeviceConnectionState Status,
    string? LastError,
    TimeSpan? RetryIn,
    DateTimeOffset Timestamp);
