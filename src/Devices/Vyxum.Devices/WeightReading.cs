namespace Vyxum.Devices;

/// <summary>One weight read from a scale, independent of brand, transport and protocol.</summary>
/// <param name="Value">The weight exactly as transmitted, including its scale ("1.250" keeps three decimals). Null for overload and underload.</param>
/// <param name="Unit">The unit as reported by the device, trimmed (for example "g", "kg", "lb"). Null when the device sent none.</param>
/// <param name="State">Stable, motion, overload or underload.</param>
/// <param name="Tare">The tare weight when the device reports it; otherwise null.</param>
/// <param name="IsNet">True for a net weight, false for gross, null when the device does not say.</param>
/// <param name="Raw">The raw frame text, kept for diagnostics.</param>
/// <param name="Timestamp">When the reading was received.</param>
public sealed record WeightReading(
    decimal? Value,
    string? Unit,
    WeightState State,
    decimal? Tare,
    bool? IsNet,
    string Raw,
    DateTimeOffset Timestamp);
