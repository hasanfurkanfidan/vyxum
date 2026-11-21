namespace Vyxum.Platform.Abstractions;

/// <summary>The connection state of a device as last known by the platform.</summary>
public enum DeviceConnectionState
{
    /// <summary>The agent is opening the connection to the device, or no reading has arrived yet.</summary>
    Connecting,

    /// <summary>The device is sending readings.</summary>
    Connected,

    /// <summary>The device or the agent it belongs to is not connected.</summary>
    Disconnected,
}

/// <summary>A snapshot of one device known to the platform.</summary>
/// <param name="DeviceId">The device id, as configured on the agent.</param>
/// <param name="AgentName">The name of the agent that reported the device; null if it never registered.</param>
/// <param name="State">The connection state.</param>
/// <param name="LastError">The last error reported for the device; null when there is none.</param>
/// <param name="LastWeight">The latest weight; null when none has arrived yet.</param>
/// <param name="LastSeen">When the platform last heard about the device.</param>
/// <param name="SupportedCommands">
/// The commands the device supports, as reported by its agent (see <see cref="DeviceCommandNames"/>); empty when none
/// or when the agent did not report them. Null is stored as empty.
/// </param>
public sealed record LiveDevice(
    string DeviceId,
    string? AgentName,
    DeviceConnectionState State,
    string? LastError,
    LiveWeight? LastWeight,
    DateTimeOffset LastSeen,
    IReadOnlyList<string>? SupportedCommands = null)
{
    /// <summary>The commands the device supports (see <see cref="DeviceCommandNames"/>); never null.</summary>
    public IReadOnlyList<string> SupportedCommands { get; init; } = SupportedCommands ?? [];
}
