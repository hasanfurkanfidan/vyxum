using System.Text;

namespace Vyxum.Devices.ToledoContinuous;

/// <summary>
/// Parses Toledo Continuous output frames: STX, status words A, B and C, six digits of displayed weight,
/// six digits of tare, CR and an optional checksum. Never throws; frames it does not understand are returned
/// with <see cref="ToledoContinuousFrame.IsValid"/> false.
/// </summary>
/// <remarks>
/// The format has a single out-of-range bit for both overload and underload. An out-of-range frame with the
/// negative sign bit set is reported as <see cref="WeightState.Underload"/>, otherwise as <see cref="WeightState.Overload"/>.
/// </remarks>
public static class ToledoContinuousParser
{
    /// <summary>The number of bytes between STX and CR.</summary>
    public const int PayloadLength = 15;

    private const byte Stx = 0x02;
    private const byte Cr = 0x0D;
    private const int FieldLength = 6;

    /// <summary>
    /// Parses one complete frame as received: STX through CR, followed by the checksum byte when
    /// <paramref name="checksumEnabled"/> is true.
    /// </summary>
    public static ToledoContinuousFrame Parse(ReadOnlySpan<byte> frame, bool checksumEnabled = false)
    {
        var expected = PayloadLength + 2 + (checksumEnabled ? 1 : 0);
        if (frame.Length != expected)
        {
            return ToledoContinuousFrame.Invalid(Text(frame), $"The frame has {frame.Length} bytes; {expected} were expected.");
        }

        if (frame[0] != Stx || frame[PayloadLength + 1] != Cr)
        {
            return ToledoContinuousFrame.Invalid(Text(frame), "The frame does not start with STX and end with CR.");
        }

        return ParseCore(frame[1..(PayloadLength + 1)], checksumEnabled ? frame[^1] : null);
    }

    /// <summary>
    /// Parses a frame read by a <see cref="FrameReader"/> in <see cref="FrameMode.StxCr"/> mode. When
    /// <paramref name="checksumEnabled"/> is true the reader must use one trailing byte, which holds the checksum.
    /// </summary>
    public static ToledoContinuousFrame Parse(Frame frame, bool checksumEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (checksumEnabled && frame.Trailer.Length != 1)
        {
            return ToledoContinuousFrame.Invalid(frame.Text, "The checksum byte is missing.");
        }

        return ParseCore(frame.Payload, checksumEnabled ? frame.Trailer[0] : null);
    }

    /// <summary>Computes the checksum of the bytes before it (STX through CR): the 2's complement of their 7-bit sum.</summary>
    public static byte ComputeChecksum(ReadOnlySpan<byte> bytes)
    {
        var sum = 0;
        foreach (var value in bytes)
        {
            sum += value;
        }

        return (byte)(-sum & 0x7F);
    }

    private static ToledoContinuousFrame ParseCore(ReadOnlySpan<byte> payload, byte? checksum)
    {
        var raw = Text(payload);
        if (payload.Length != PayloadLength)
        {
            return ToledoContinuousFrame.Invalid(raw, $"The frame content has {payload.Length} bytes; {PayloadLength} were expected.");
        }

        if (checksum is { } received)
        {
            var sum = Stx + Cr + received;
            foreach (var b in payload)
            {
                sum += b;
            }

            if ((sum & 0x7F) != 0)
            {
                return ToledoContinuousFrame.Invalid(raw, "The checksum does not match.");
            }
        }

        var swa = payload[0];
        var swb = payload[1];
        var swc = payload[2];

        // Bit 7 is never set in a 7-bit status word; seeing it usually means wrong data bits or parity.
        if ((swa & 0xE0) != 0x20 || (swb & 0xA0) != 0x20 || (swc & 0xE0) != 0x20)
        {
            return ToledoContinuousFrame.Invalid(raw, "The status words are not valid.");
        }

        var outOfRange = (swb & 0x04) != 0;
        var negative = (swb & 0x02) != 0;
        var decimalCode = swa & 0x07;

        if (!TryReadField(payload.Slice(3, FieldLength), out var weightDigits) && !outOfRange)
        {
            return ToledoContinuousFrame.Invalid(raw, "The weight field is not a number.");
        }

        if (!TryReadField(payload.Slice(3 + FieldLength, FieldLength), out var tareDigits))
        {
            return ToledoContinuousFrame.Invalid(raw, "The tare field is not a number.");
        }

        var state = outOfRange
            ? (negative ? WeightState.Underload : WeightState.Overload)
            : (swb & 0x08) != 0 ? WeightState.Motion : WeightState.Stable;

        decimal? value = outOfRange ? null : Scale(weightDigits, decimalCode, negative);

        return new ToledoContinuousFrame(
            IsValid: true,
            InvalidReason: null,
            Raw: raw,
            Value: value,
            Unit: Unit(swb, swc),
            State: state,
            Tare: Scale(tareDigits, decimalCode, negative: false),
            IsNet: (swb & 0x01) != 0,
            PrintRequested: (swc & 0x08) != 0,
            ExpandedData: (swc & 0x10) != 0,
            PowerUpNotZeroed: (swb & 0x40) != 0);
    }

    /// <summary>Reads six digits with optional leading or trailing spaces. All spaces read as zero.</summary>
    private static bool TryReadField(ReadOnlySpan<byte> field, out int digits)
    {
        digits = 0;
        var start = 0;
        var end = field.Length;
        while (start < end && field[start] == (byte)' ')
        {
            start++;
        }

        while (end > start && field[end - 1] == (byte)' ')
        {
            end--;
        }

        for (var i = start; i < end; i++)
        {
            var value = field[i];
            if (value is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            digits = (digits * 10) + (value - '0');
        }

        return true;
    }

    /// <summary>Applies the decimal location code of status word A, keeping the number of decimals.</summary>
    private static decimal Scale(int digits, int decimalCode, bool negative)
    {
        var isNegative = negative && digits != 0;
        return decimalCode switch
        {
            0 => new decimal(digits * 100, 0, 0, isNegative, 0),
            1 => new decimal(digits * 10, 0, 0, isNegative, 0),
            _ => new decimal(digits, 0, 0, isNegative, (byte)(decimalCode - 2)),
        };
    }

    private static string? Unit(byte swb, byte swc) => (swc & 0x07) switch
    {
        0 => (swb & 0x10) != 0 ? "kg" : "lb",
        1 => "g",
        2 => "t",
        3 => "oz",
        4 => "ozt",
        5 => "dwt",
        6 => "ton",
        _ => null,
    };

    private static string Text(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);
}
