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
}

/// <summary>Sent after every (re)connect.</summary>
/// <param name="AgentName">A name for the agent, usually the computer name.</param>
/// <param name="Version">The agent version.</param>
/// <param name="DeviceIds">The devices the agent serves.</param>
public sealed record AgentRegistration(string AgentName, string? Version, IReadOnlyList<string> DeviceIds);

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
