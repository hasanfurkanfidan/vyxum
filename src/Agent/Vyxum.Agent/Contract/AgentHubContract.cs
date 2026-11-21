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
}

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
public sealed record AgentRegistration(
    string AgentName,
    string? Version,
    IReadOnlyList<string> DeviceIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DeviceCommands = null);

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
