using Vyxum.Devices.GenericAscii;
using Vyxum.Devices.MtSics;
using Vyxum.Devices.ToledoContinuous;
using Vyxum.Devices.Transports;

namespace Vyxum.Devices.Discovery;

/// <summary>Data bits, parity and stop bits of a serial link, for example 8N1.</summary>
/// <param name="DataBits">Data bits.</param>
/// <param name="Parity">Parity.</param>
/// <param name="StopBits">Stop bits.</param>
public sealed record SerialFraming(int DataBits, SerialParity Parity, SerialStopBits StopBits)
{
    /// <summary>8 data bits, no parity, 1 stop bit.</summary>
    public static SerialFraming EightNoneOne { get; } = new(8, SerialParity.None, SerialStopBits.One);

    /// <summary>7 data bits, even parity, 1 stop bit.</summary>
    public static SerialFraming SevenEvenOne { get; } = new(7, SerialParity.Even, SerialStopBits.One);

    /// <summary>7 data bits, odd parity, 1 stop bit.</summary>
    public static SerialFraming SevenOddOne { get; } = new(7, SerialParity.Odd, SerialStopBits.One);

    /// <summary>The usual short form, for example <c>8N1</c>.</summary>
    public override string ToString()
    {
        var parity = Parity switch
        {
            SerialParity.None => 'N',
            SerialParity.Odd => 'O',
            SerialParity.Even => 'E',
            SerialParity.Mark => 'M',
            _ => 'S',
        };
        var stop = StopBits switch
        {
            SerialStopBits.One => "1",
            SerialStopBits.OnePointFive => "1.5",
            _ => "2",
        };
        return $"{DataBits}{parity}{stop}";
    }
}

/// <summary>Settings of a <see cref="PortScanner"/>. The defaults are what the product uses; tests replace the factories.</summary>
public sealed class PortScannerOptions
{
    /// <summary>Baud rates tried on each serial port, most likely first. Default: 9600, 19200, 4800, 2400, 38400, 115200.</summary>
    public IReadOnlyList<int> BaudRates { get; set; } = [9600, 19200, 4800, 2400, 38400, 115200];

    /// <summary>
    /// Framings tried at each baud rate, most likely first. Default: 8N1, 7E1, 7O1. The first one is the primary framing:
    /// see <see cref="PortScanner"/> for how the others are shortened.
    /// </summary>
    public IReadOnlyList<SerialFraming> Framings { get; set; } =
        [SerialFraming.EightNoneOne, SerialFraming.SevenEvenOne, SerialFraming.SevenOddOne];

    /// <summary>How long to listen for output the device sends on its own. Default: 700 ms.</summary>
    public TimeSpan ListenTime { get; set; } = TimeSpan.FromMilliseconds(700);

    /// <summary>How long an active probe waits for each answer. Default: 500 ms.</summary>
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long opening a port may take before the port is reported as failed. Default: 3 seconds.</summary>
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The longest time spent on one port; after it the port is reported as not found, and a port whose driver does not
    /// stop within a few more seconds is abandoned and reported as an error. Default: 25 seconds.
    /// </summary>
    public TimeSpan PortTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>How many ports are scanned at the same time. Default: 8.</summary>
    public int MaxParallelPorts { get; set; } = 8;

    /// <summary>TCP connect timeout of the network probe. Default: 3 seconds.</summary>
    public TimeSpan TcpConnectTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Lists the serial ports of the computer. Default: <see cref="SerialPorts.GetPortNames"/>.</summary>
    public Func<IReadOnlyList<string>> GetPortNames { get; set; } = SerialPorts.GetPortNames;

    /// <summary>Creates the transport of one serial attempt. Default: <see cref="SerialTransport"/>.</summary>
    public Func<SerialTransportOptions, ITransport> CreateSerialTransport { get; set; } = options => new SerialTransport(options);

    /// <summary>Creates the transport of the network probe. Default: <see cref="TcpTransport"/>.</summary>
    public Func<TcpTransportOptions, ITransport> CreateTcpTransport { get; set; } = options => new TcpTransport(options);

    /// <summary>Recognisers for output sent on its own, tried in order. Default: Toledo Continuous, then generic ASCII.</summary>
    public IReadOnlyList<IPassiveProbe> PassiveProbes { get; set; } = [new ToledoContinuousProbe(), new GenericAsciiProbe()];

    /// <summary>Probes that send read-only commands, tried in order after listening. Default: MT-SICS (<c>SI</c>, <c>I4</c>).</summary>
    public IReadOnlyList<IActiveProbe> ActiveProbes { get; set; } = [new MtSicsProbe()];
}
