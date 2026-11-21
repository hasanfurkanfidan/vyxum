namespace Vyxum.Devices;

/// <summary>The state a scale reports together with a weight.</summary>
public enum WeightState
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
