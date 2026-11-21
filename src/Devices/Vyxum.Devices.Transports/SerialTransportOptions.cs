namespace Vyxum.Devices.Transports;

/// <summary>Parity of a serial link.</summary>
public enum SerialParity
{
    /// <summary>No parity bit.</summary>
    None,

    /// <summary>Odd parity.</summary>
    Odd,

    /// <summary>Even parity.</summary>
    Even,

    /// <summary>Parity bit always 1.</summary>
    Mark,

    /// <summary>Parity bit always 0.</summary>
    Space,
}

/// <summary>Stop bits of a serial link.</summary>
public enum SerialStopBits
{
    /// <summary>One stop bit.</summary>
    One,

    /// <summary>One and a half stop bits.</summary>
    OnePointFive,

    /// <summary>Two stop bits.</summary>
    Two,
}

/// <summary>Flow control of a serial link.</summary>
public enum SerialHandshake
{
    /// <summary>No flow control.</summary>
    None,

    /// <summary>Software flow control (XON/XOFF).</summary>
    XOnXOff,

    /// <summary>Hardware flow control (RTS/CTS).</summary>
    RequestToSend,

    /// <summary>Both hardware and software flow control.</summary>
    RequestToSendXOnXOff,
}

/// <summary>Settings for <see cref="SerialTransport"/>.</summary>
public sealed record SerialTransportOptions
{
    /// <summary>The port name, for example <c>COM3</c> or <c>/dev/ttyUSB0</c>.</summary>
    public required string PortName { get; init; }

    /// <summary>Baud rate. Default: 9600.</summary>
    public int BaudRate { get; init; } = 9600;

    /// <summary>Data bits (5 to 8). Default: 8.</summary>
    public int DataBits { get; init; } = 8;

    /// <summary>Parity. Default: none.</summary>
    public SerialParity Parity { get; init; } = SerialParity.None;

    /// <summary>Stop bits. Default: one.</summary>
    public SerialStopBits StopBits { get; init; } = SerialStopBits.One;

    /// <summary>Flow control. Default: none.</summary>
    public SerialHandshake Handshake { get; init; } = SerialHandshake.None;

    /// <summary>Raises the DTR line when the port opens. Default: true.</summary>
    public bool DtrEnable { get; init; } = true;

    /// <summary>Raises the RTS line when the port opens (ignored when the handshake uses RTS). Default: true.</summary>
    public bool RtsEnable { get; init; } = true;

    /// <summary>
    /// How long a write may take before it fails. Some virtual ports (for example Bluetooth serial ports without a
    /// connected device) accept the open but never complete a write; without a limit the write would hang. Default: 2 seconds.
    /// </summary>
    public TimeSpan WriteTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(2);
}
