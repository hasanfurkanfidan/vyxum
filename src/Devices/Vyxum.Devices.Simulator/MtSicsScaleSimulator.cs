using System.Collections.Concurrent;
using System.Globalization;

namespace Vyxum.Devices.Simulator;

/// <summary>
/// Answers MT-SICS commands the way a METTLER TOLEDO indicator does: <c>S</c>, <c>SI</c> and <c>SIR</c> with the weight,
/// <c>Z</c> by zeroing, <c>I4</c> with the serial number, anything else with <c>ES</c>. The value field is right-aligned in
/// ten characters: <c>S S        572 g</c>, <c>S D      1.250 kg</c>, <c>S S     -12.34 g</c>, <c>S +</c>, <c>S -</c>.
/// Every received command is recorded; commands that must never be sent (tare, immediate zero, reset) are recorded
/// separately in <see cref="ForbiddenCommands"/> so a test can assert that a driver never sent them.
/// </summary>
/// <remarks>
/// <c>S</c> is answered like <c>SI</c> (a real device would wait for stability). <c>SIR</c> starts a stream of weight lines
/// every <see cref="StreamInterval"/> that ends when the next command arrives. <c>Z</c> takes the current weight as the new
/// zero point, so later weights are reported relative to it, and answers <c>Z A</c>; while the weight is in motion it
/// answers <c>Z I</c>, and on overload or underload <c>Z +</c> or <c>Z -</c>. <c>@</c> is answered like the IND231 does
/// (the serial number); T, TI and ZI are answered with <c>ES</c>.
/// </remarks>
public sealed class MtSicsScaleSimulator : ISimulatedDevice
{
    private const int ValueWidth = 10;
    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.Ordinal) { "T", "TI", "ZI", "@" };

    private readonly IWeightSource _source;
    private readonly ConcurrentQueue<string> _received = new();
    private readonly ConcurrentQueue<string> _forbidden = new();
    private volatile bool _firstCommandError;
    private volatile bool _silent;
    private volatile bool _busy;
    private readonly Lock _zeroLock = new();
    private decimal _zeroOffset;

    /// <summary>Creates the simulator.</summary>
    public MtSicsScaleSimulator(IWeightSource source, string serialNumber = "SIM0000001")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        _source = source;
        SerialNumber = serialNumber;
    }

    /// <summary>The serial number answered to <c>I4</c>.</summary>
    public string SerialNumber { get; }

    /// <summary>Scenario: answer the first command of every session with <c>ES</c>, like the IND231 recording.</summary>
    public bool FirstCommandReturnsSyntaxError
    {
        get => _firstCommandError;
        set => _firstCommandError = value;
    }

    /// <summary>Scenario: read commands but never answer.</summary>
    public bool Silent
    {
        get => _silent;
        set => _silent = value;
    }

    /// <summary>Scenario: answer weight commands with <c>S I</c> (busy).</summary>
    public bool Busy
    {
        get => _busy;
        set => _busy = value;
    }

    /// <summary>The time between lines after <c>SIR</c>. Default: 20 ms.</summary>
    public TimeSpan StreamInterval
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMilliseconds(20);

    /// <summary>All commands received, in order, over all sessions.</summary>
    public IReadOnlyList<string> ReceivedCommands => [.. _received];

    /// <summary>The received commands that must never be sent: tare, immediate zero and reset (T, TI, ZI, @).</summary>
    public IReadOnlyList<string> ForbiddenCommands => [.. _forbidden];

    /// <summary>How many <c>Z</c> commands were received, over all sessions.</summary>
    public int ZeroCommandCount => _received.Count(c => c == "Z");

    /// <summary>The zero point set by the last accepted <c>Z</c>; 0 until then. It survives reconnects, like on a real device.</summary>
    public decimal ZeroOffset
    {
        get
        {
            lock (_zeroLock)
            {
                return _zeroOffset;
            }
        }
    }

    /// <summary>Formats a weight as an MT-SICS answer line (no terminator).</summary>
    public static string FormatWeightLine(SimulatedWeight weight) => weight.State switch
    {
        WeightState.Overload => "S +",
        WeightState.Underload => "S -",
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"S {(weight.State == WeightState.Stable ? 'S' : 'D')} {weight.Value.ToString(CultureInfo.InvariantCulture).PadLeft(ValueWidth)} {weight.Unit}"),
    };

    /// <inheritdoc />
    public async Task RunSessionAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = new LineReader(stream);
        var writer = new LineWriter(stream);
        var first = true;
        CancellationTokenSource? streamCts = null;
        Task? streaming = null;
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var command = line.Trim();
                if (command.Length == 0)
                {
                    continue;
                }

                _received.Enqueue(command);
                var name = command.Split(' ', 2)[0];
                if (ForbiddenNames.Contains(name))
                {
                    _forbidden.Enqueue(command);
                }

                await StopStreamAsync().ConfigureAwait(false);

                var wasFirst = first;
                first = false;
                if (_silent)
                {
                    continue;
                }

                if (wasFirst && _firstCommandError)
                {
                    await writer.WriteLineAsync("ES", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                switch (name)
                {
                    case "S" or "SI":
                        await writer.WriteLineAsync(WeightLine(), cancellationToken).ConfigureAwait(false);
                        break;
                    case "SIR":
                        streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        streaming = StreamAsync(writer, streamCts.Token);
                        break;
                    case "Z" when command == "Z":
                        await writer.WriteLineAsync(Zero(), cancellationToken).ConfigureAwait(false);
                        break;
                    case "I4" or "@":
                        await writer.WriteLineAsync($"I4 A \"{SerialNumber}\"", cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        await writer.WriteLineAsync("ES", cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        }
        finally
        {
            await StopStreamAsync().ConfigureAwait(false);
        }

        async Task StopStreamAsync()
        {
            if (streamCts is null)
            {
                return;
            }

            await streamCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await streaming!.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The stream ends with the connection.
            }

            streamCts.Dispose();
            streamCts = null;
            streaming = null;
        }
    }

    private string WeightLine() => _busy ? "S I" : FormatWeightLine(Relative(_source.Next()));

    private SimulatedWeight Relative(SimulatedWeight weight)
    {
        lock (_zeroLock)
        {
            return weight with { Value = weight.Value - _zeroOffset };
        }
    }

    private string Zero()
    {
        var weight = _source.Next();
        switch (weight.State)
        {
            case WeightState.Motion:
                return "Z I";
            case WeightState.Overload:
                return "Z +";
            case WeightState.Underload:
                return "Z -";
            default:
                lock (_zeroLock)
                {
                    _zeroOffset = weight.Value;
                }

                return "Z A";
        }
    }

    private async Task StreamAsync(LineWriter writer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(StreamInterval);
        do
        {
            if (!_silent)
            {
                await writer.WriteLineAsync(WeightLine(), cancellationToken).ConfigureAwait(false);
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }
}
