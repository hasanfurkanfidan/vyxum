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
}

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
public sealed record AgentRegistration(
    string AgentName,
    string? Version,
    IReadOnlyList<string> DeviceIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DeviceCommands = null);

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
