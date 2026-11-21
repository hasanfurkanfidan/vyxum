using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// The wire contract between Vyxum Agent and the platform's agent hub. Enums travel as strings.
/// </summary>
public static class AgentHubContract
{
    /// <summary>The hub path.</summary>
    public const string Path = "/hubs/agent";

    /// <summary>Header that carries the agent key.</summary>
    public const string KeyHeader = "X-Vyxum-Agent-Key";

    /// <summary>Query parameter that carries the agent key for clients that cannot send headers.</summary>
    public const string KeyQueryParameter = "access_token";

    /// <summary>Hub method: <c>Register(AgentRegistration)</c>.</summary>
    public const string Register = "Register";

    /// <summary>Hub method: <c>ReportReading(string deviceId, AgentReading reading)</c>.</summary>
    public const string ReportReading = "ReportReading";

    /// <summary>Hub method: <c>ReportStatus(string deviceId, AgentDeviceStatus status)</c>.</summary>
    public const string ReportStatus = "ReportStatus";
}

/// <summary>Sent by an agent after connecting.</summary>
/// <param name="AgentName">A name for the agent, usually the computer name.</param>
/// <param name="Version">The agent version.</param>
/// <param name="DeviceIds">The devices the agent serves.</param>
public sealed record AgentRegistration(string AgentName, string? Version, IReadOnlyList<string> DeviceIds);

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
