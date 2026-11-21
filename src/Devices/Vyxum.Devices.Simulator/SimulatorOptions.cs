namespace Vyxum.Devices.Simulator;

/// <summary>The kind of device to simulate.</summary>
public enum SimulatorKind
{
    /// <summary>An MT-SICS indicator that answers commands (<see cref="MtSicsScaleSimulator"/>).</summary>
    MtSics,

    /// <summary>An indicator that sends Toledo Continuous frames on its own (<see cref="ToledoContinuousSimulator"/>).</summary>
    ToledoContinuous,

    /// <summary>Replays the lines of a recording file (<see cref="LineReplaySimulator"/>).</summary>
    Replay,
}

/// <summary>Settings that describe a simulated device, so a host can configure one from its settings file.</summary>
public sealed record SimulatorOptions
{
    /// <summary>The kind of device. Default: MT-SICS.</summary>
    public SimulatorKind Kind { get; init; } = SimulatorKind.MtSics;

    /// <summary>The weight value (MT-SICS and Toledo). Its decimals are kept on the wire. Default: 0.</summary>
    public decimal Weight { get; init; }

    /// <summary>The unit (MT-SICS and Toledo). Default: g.</summary>
    public string Unit { get; init; } = "g";

    /// <summary>The weight state (MT-SICS and Toledo). Default: stable.</summary>
    public WeightState State { get; init; } = WeightState.Stable;

    /// <summary>The serial number answered to I4 (MT-SICS).</summary>
    public string SerialNumber { get; init; } = "SIM0000001";

    /// <summary>Scenario: the first command of each session is answered with ES (MT-SICS).</summary>
    public bool FirstCommandReturnsSyntaxError { get; init; }

    /// <summary>Scenario: never answer (MT-SICS) or never send (Toledo).</summary>
    public bool Silent { get; init; }

    /// <summary>Scenario: answer weight commands with S I (MT-SICS).</summary>
    public bool Busy { get; init; }

    /// <summary>Toledo only: add a checksum byte after CR.</summary>
    public bool Checksum { get; init; }

    /// <summary>The time between frames (Toledo, replay in interval mode) or between SIR lines (MT-SICS). Default: 50 ms.</summary>
    public TimeSpan Interval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMilliseconds(50);

    /// <summary>Replay only: the recording file.</summary>
    public string? RecordingPath { get; init; }

    /// <summary>Replay only: when lines are sent. Default: on request.</summary>
    public ReplayMode ReplayMode { get; init; } = ReplayMode.OnRequest;

    /// <summary>Replay only: start again after the last line. Default: true.</summary>
    public bool Loop { get; init; } = true;
}

/// <summary>Creates simulated devices and transports from <see cref="SimulatorOptions"/>.</summary>
public static class SimulatorFactory
{
    /// <summary>Creates the device the options describe.</summary>
    public static ISimulatedDevice CreateDevice(SimulatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        switch (options.Kind)
        {
            case SimulatorKind.MtSics:
                return new MtSicsScaleSimulator(new FixedWeightSource(options.Weight, options.Unit, options.State), options.SerialNumber)
                {
                    FirstCommandReturnsSyntaxError = options.FirstCommandReturnsSyntaxError,
                    Silent = options.Silent,
                    Busy = options.Busy,
                    StreamInterval = options.Interval,
                };
            case SimulatorKind.ToledoContinuous:
                return new ToledoContinuousSimulator(new FixedWeightSource(options.Weight, options.Unit, options.State), options.Interval, options.Checksum)
                {
                    Silent = options.Silent,
                };
            case SimulatorKind.Replay:
                if (string.IsNullOrWhiteSpace(options.RecordingPath))
                {
                    throw new ArgumentException("RecordingPath is required for a replay simulator.", nameof(options));
                }

                return LineReplaySimulator.FromFile(options.RecordingPath, options.ReplayMode, options.Interval, options.Loop);
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Kind, "Unknown simulator kind.");
        }
    }

    /// <summary>Creates an in-memory transport to a new device the options describe.</summary>
    public static SimulatedTransport CreateTransport(SimulatorOptions options) =>
        new(CreateDevice(options), $"simulator ({options.Kind})");
}
