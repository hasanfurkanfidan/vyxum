using System.Globalization;
using System.Text;

namespace Vyxum.Devices.Simulator;

/// <summary>
/// Emits Toledo Continuous frames on its own, one per <see cref="Interval"/>: STX, status words A, B and C, six digits
/// of weight, six digits of tare, CR and optionally a checksum (the 2's complement of the 7-bit sum of STX through CR).
/// Listen-only: whatever the host writes is read and ignored.
/// </summary>
public sealed class ToledoContinuousSimulator : ISimulatedDevice
{
    private const byte Stx = 0x02;
    private const byte Cr = 0x0D;

    private readonly IWeightSource _source;
    private volatile bool _silent;

    /// <summary>Creates the simulator.</summary>
    public ToledoContinuousSimulator(IWeightSource source, TimeSpan? interval = null, bool checksum = false, decimal tare = 0m)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        Interval = interval ?? TimeSpan.FromMilliseconds(50);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Interval, TimeSpan.Zero);
        Checksum = checksum;
        Tare = tare;
    }

    /// <summary>The time between frames.</summary>
    public TimeSpan Interval { get; }

    /// <summary>True when a checksum byte follows CR.</summary>
    public bool Checksum { get; }

    /// <summary>The tare weight written in every frame, with the decimals of the weight.</summary>
    public decimal Tare { get; }

    /// <summary>Scenario: stop emitting frames while true (the connection stays up).</summary>
    public bool Silent
    {
        get => _silent;
        set => _silent = value;
    }

    /// <summary>Builds one frame: STX through CR, plus the checksum when asked.</summary>
    public static byte[] BuildFrame(SimulatedWeight weight, bool checksum, decimal tare = 0m)
    {
        var outOfRange = weight.State is WeightState.Overload or WeightState.Underload;
        var negative = weight.State == WeightState.Underload || (!outOfRange && weight.Value < 0m);
        var scale = outOfRange ? 0 : (decimal.GetBits(weight.Value)[3] >> 16) & 0xFF;
        if (scale > 5)
        {
            throw new ArgumentException("At most five decimals can be sent.", nameof(weight));
        }

        var (unitBit, unitCode) = weight.Unit switch
        {
            "kg" => (0x10, 0),
            "lb" => (0, 0),
            "g" => (0, 1),
            "t" => (0, 2),
            "oz" => (0, 3),
            "ozt" => (0, 4),
            "dwt" => (0, 5),
            "ton" => (0, 6),
            _ => throw new ArgumentException($"Unit '{weight.Unit}' cannot be sent.", nameof(weight)),
        };

        var swa = 0x20 | (1 << 3) | (scale + 2);
        var swb = 0x20 | unitBit | (negative ? 0x02 : 0) | (outOfRange ? 0x04 : 0) | (weight.State == WeightState.Motion ? 0x08 : 0);
        var swc = 0x20 | unitCode;

        var factor = (decimal)Math.Pow(10, scale);
        var weightDigits = outOfRange ? 0m : Math.Min(999_999m, decimal.Truncate(Math.Abs(weight.Value) * factor));
        var tareDigits = Math.Min(999_999m, decimal.Truncate(Math.Abs(tare) * factor));

        var bytes = new List<byte> { Stx, (byte)swa, (byte)swb, (byte)swc };
        bytes.AddRange(Encoding.ASCII.GetBytes(weightDigits.ToString("000000", CultureInfo.InvariantCulture)));
        bytes.AddRange(Encoding.ASCII.GetBytes(tareDigits.ToString("000000", CultureInfo.InvariantCulture)));
        bytes.Add(Cr);
        if (checksum)
        {
            var sum = 0;
            foreach (var b in bytes)
            {
                sum += b;
            }

            bytes.Add((byte)(-sum & 0x7F));
        }

        return [.. bytes];
    }

    /// <inheritdoc />
    public async Task RunSessionAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var writer = new LineWriter(stream);
        using var timer = new PeriodicTimer(Interval);

        // Read and ignore what the host sends; it also tells us when the host has closed the connection.
        var drain = DrainAsync(stream, cancellationToken);
        do
        {
            if (!_silent)
            {
                await writer.WriteAsync(BuildFrame(_source.Next(), Checksum, Tare), cancellationToken).ConfigureAwait(false);
            }
        }
        while (!drain.IsCompleted && await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task DrainAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[64];
        try
        {
            while (await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (Exception)
        {
            // The connection ended.
        }
    }
}
