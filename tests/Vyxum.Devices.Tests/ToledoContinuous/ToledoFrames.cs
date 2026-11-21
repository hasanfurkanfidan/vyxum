using System.Text;

namespace Vyxum.Devices.Tests.ToledoContinuous;

/// <summary>Builds Toledo Continuous frames bit by bit, so tests state what they mean instead of raw bytes.</summary>
internal sealed record ToledoFrames
{
    public int DecimalCode { get; init; } = 2;

    /// <summary>Increment size bits 3-4 of status word A: 1 = x1, 2 = x2, 3 = x5.</summary>
    public int IncrementCode { get; init; } = 1;

    public bool Net { get; init; }

    public bool Negative { get; init; }

    public bool OutOfRange { get; init; }

    public bool Motion { get; init; }

    public bool Kg { get; init; } = true;

    public bool PowerUpNotZeroed { get; init; }

    public int UnitExtension { get; init; }

    public bool PrintRequest { get; init; }

    public bool Expanded { get; init; }

    public string Weight { get; init; } = "001234";

    public string Tare { get; init; } = "000000";

    public byte Swa => (byte)(0x20 | (IncrementCode << 3) | DecimalCode);

    public byte Swb => (byte)(0x20 | Bit(Net, 0) | Bit(Negative, 1) | Bit(OutOfRange, 2) | Bit(Motion, 3) | Bit(Kg, 4) | Bit(PowerUpNotZeroed, 6));

    public byte Swc => (byte)(0x20 | UnitExtension | Bit(PrintRequest, 3) | Bit(Expanded, 4));

    /// <summary>The payload between STX and CR.</summary>
    public string Payload => Encoding.Latin1.GetString([Swa, Swb, Swc]) + Weight + Tare;

    /// <summary>STX, payload, CR and, when asked, the checksum.</summary>
    public byte[] Build(bool checksum = false)
    {
        var bytes = new List<byte> { 0x02 };
        bytes.AddRange(Encoding.Latin1.GetBytes(Payload));
        bytes.Add(0x0D);
        if (checksum)
        {
            var sum = bytes.Sum(b => b);
            bytes.Add((byte)(-sum & 0x7F));
        }

        return [.. bytes];
    }

    private static int Bit(bool value, int bit) => value ? 1 << bit : 0;
}
