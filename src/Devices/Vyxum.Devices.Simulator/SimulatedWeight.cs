namespace Vyxum.Devices.Simulator;

/// <summary>One weight a simulated device reports.</summary>
/// <param name="Value">The value. Its scale is kept on the wire (1.250 is sent with three decimals). Ignored for overload and underload.</param>
/// <param name="Unit">The unit as the device writes it ("g", "kg", "lb").</param>
/// <param name="State">Stable, motion, overload or underload.</param>
public readonly record struct SimulatedWeight(decimal Value, string Unit, WeightState State = WeightState.Stable);
