namespace Vyxum.Platform.Abstractions;

/// <summary>The state reported together with a live weight.</summary>
public enum LiveWeightState
{
    /// <summary>The weight is stable.</summary>
    Stable,

    /// <summary>The weight is still changing.</summary>
    Motion,

    /// <summary>The load is above the scale's range. No value is available.</summary>
    Overload,

    /// <summary>The load is below the scale's range. No value is available.</summary>
    Underload,
}

/// <summary>The latest weight of a device, independent of brand, transport and protocol.</summary>
/// <param name="DeviceId">The device the weight belongs to.</param>
/// <param name="Value">The weight as transmitted, including its scale. Null for overload and underload.</param>
/// <param name="Unit">The unit reported by the device (for example "g", "kg"); null when it sent none.</param>
/// <param name="State">Stable, motion, overload or underload.</param>
/// <param name="Tare">The tare weight when the device reports it; otherwise null.</param>
/// <param name="IsNet">True for a net weight, false for gross, null when the device does not say.</param>
/// <param name="Timestamp">When the agent read the weight from the device.</param>
/// <param name="ReceivedAt">When the platform received the weight from the agent.</param>
public sealed record LiveWeight(
    string DeviceId,
    decimal? Value,
    string? Unit,
    LiveWeightState State,
    decimal? Tare,
    bool? IsNet,
    DateTimeOffset Timestamp,
    DateTimeOffset ReceivedAt);
