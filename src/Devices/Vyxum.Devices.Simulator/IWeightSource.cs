using System.Globalization;

namespace Vyxum.Devices.Simulator;

/// <summary>Supplies the weights a simulated device reports. <see cref="Next"/> is called once per answer or frame.</summary>
public interface IWeightSource
{
    /// <summary>Returns the next weight and advances the source.</summary>
    SimulatedWeight Next();
}

/// <summary>Always the same weight.</summary>
public sealed class FixedWeightSource : IWeightSource
{
    private readonly SimulatedWeight _weight;

    /// <summary>Creates a source from a weight.</summary>
    public FixedWeightSource(SimulatedWeight weight) => _weight = weight;

    /// <summary>Creates a source from a value, unit and state.</summary>
    public FixedWeightSource(decimal value, string unit = "g", WeightState state = WeightState.Stable)
        : this(new SimulatedWeight(value, unit, state))
    {
    }

    /// <inheritdoc />
    public SimulatedWeight Next() => _weight;
}

/// <summary>A scripted sequence: one step per call. After the last step it repeats the last one, or starts over when looping.</summary>
public sealed class ScriptedWeightSource : IWeightSource
{
    private readonly SimulatedWeight[] _steps;
    private readonly bool _loop;
    private readonly Lock _lock = new();
    private int _index;

    /// <summary>Creates the source. At least one step is required.</summary>
    public ScriptedWeightSource(IEnumerable<SimulatedWeight> steps, bool loop = false)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps = [.. steps];
        if (_steps.Length == 0)
        {
            throw new ArgumentException("At least one step is required.", nameof(steps));
        }

        _loop = loop;
    }

    /// <inheritdoc />
    public SimulatedWeight Next()
    {
        lock (_lock)
        {
            var step = _steps[_index];
            if (_index < _steps.Length - 1)
            {
                _index++;
            }
            else if (_loop)
            {
                _index = 0;
            }

            return step;
        }
    }
}

/// <summary>
/// Replays the weights of a recording in MT-SICS text form (<c>S S 572 g</c>, <c>S D 1.250 kg</c>, <c>S +</c>, <c>S -</c>).
/// Other lines (ES, I4, comments) are skipped. After the last weight it repeats it, or starts over when looping.
/// </summary>
public sealed class RecordedWeightSource : IWeightSource
{
    private readonly ScriptedWeightSource _inner;

    /// <summary>Creates the source from recorded lines.</summary>
    public RecordedWeightSource(IEnumerable<string> lines, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var weights = lines.Select(TryParse).Where(w => w is not null).Select(w => w!.Value).ToList();
        if (weights.Count == 0)
        {
            throw new ArgumentException("The recording contains no weight lines.", nameof(lines));
        }

        _inner = new ScriptedWeightSource(weights, loop);
    }

    /// <summary>Creates the source from a recording file (see <see cref="LineReplaySimulator.ReadRecording"/> for the format).</summary>
    public static RecordedWeightSource FromFile(string path, bool loop = true) =>
        new(LineReplaySimulator.ReadRecording(path), loop);

    /// <inheritdoc />
    public SimulatedWeight Next() => _inner.Next();

    private static SimulatedWeight? TryParse(string line)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || tokens[0] != "S")
        {
            return null;
        }

        if (tokens.Length == 2)
        {
            return tokens[1] switch
            {
                "+" => new SimulatedWeight(0m, "g", WeightState.Overload),
                "-" => new SimulatedWeight(0m, "g", WeightState.Underload),
                _ => null,
            };
        }

        if (tokens.Length > 4 || tokens[1] is not ("S" or "D")
            || !decimal.TryParse(tokens[2], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return new SimulatedWeight(value, tokens.Length == 4 ? tokens[3] : "g", tokens[1] == "S" ? WeightState.Stable : WeightState.Motion);
    }
}
