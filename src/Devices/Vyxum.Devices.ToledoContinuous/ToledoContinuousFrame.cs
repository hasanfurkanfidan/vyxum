namespace Vyxum.Devices.ToledoContinuous;

/// <summary>One parsed Toledo Continuous frame, or the reason it was rejected.</summary>
/// <param name="IsValid">True when the frame was understood.</param>
/// <param name="InvalidReason">Why the frame was rejected; null when <paramref name="IsValid"/> is true.</param>
/// <param name="Raw">The frame text without STX, CR and checksum. Each byte maps to one character (Latin-1).</param>
/// <param name="Value">The displayed weight with the decimal location applied (scale kept). Null when out of range or invalid.</param>
/// <param name="Unit">"kg", "lb", "g", "t", "oz", "ozt", "dwt" or "ton"; null for custom units or an invalid frame.</param>
/// <param name="State">Stable, motion, overload or underload.</param>
/// <param name="Tare">The tare with the decimal location applied. Null for an invalid frame.</param>
/// <param name="IsNet">True for a net weight, false for gross.</param>
/// <param name="PrintRequested">Status word C bit 3: the operator pressed print.</param>
/// <param name="ExpandedData">Status word C bit 4: the indicator shows expanded (x10) data. The value is reported as transmitted.</param>
/// <param name="PowerUpNotZeroed">Status word B bit 6: the scale has not been zeroed since power-up.</param>
public sealed record ToledoContinuousFrame(
    bool IsValid,
    string? InvalidReason,
    string Raw,
    decimal? Value = null,
    string? Unit = null,
    WeightState State = WeightState.Stable,
    decimal? Tare = null,
    bool IsNet = false,
    bool PrintRequested = false,
    bool ExpandedData = false,
    bool PowerUpNotZeroed = false)
{
    /// <summary>Creates a rejected frame.</summary>
    public static ToledoContinuousFrame Invalid(string raw, string reason) => new(false, reason, raw);

    /// <summary>Converts a valid frame to a reading. Throws for an invalid frame.</summary>
    public WeightReading ToReading(DateTimeOffset timestamp)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException($"An invalid frame cannot be converted to a reading: {InvalidReason}");
        }

        return new WeightReading(Value, Unit, State, Tare, IsNet, Raw, timestamp);
    }
}
