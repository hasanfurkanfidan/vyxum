using Vyxum.Devices;
using Vyxum.Devices.Simulator;
using Vyxum.Devices.Transports;

namespace Vyxum.Agent.Configuration;

/// <summary>The agent settings, bound from the <c>Vyxum:Agent</c> configuration section.</summary>
public sealed class AgentSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Vyxum:Agent";

    /// <summary>
    /// The default server address. It is the address ASP.NET Core listens on when no URL is configured,
    /// which is how the Vyxum web application runs on a single computer.
    /// </summary>
    public const string DefaultServerUrl = "http://localhost:5000";

    /// <summary>The address of the Vyxum web application. Default: <see cref="DefaultServerUrl"/>.</summary>
    public string ServerUrl { get; set; } = DefaultServerUrl;

    /// <summary>
    /// The agent key, sent in the <c>X-Vyxum-Agent-Key</c> header. Needed only when the web application has a key
    /// configured (decision V-26). Never logged.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>The name shown for this agent in the web application. Default: the computer name.</summary>
    public string? AgentName { get; set; }

    /// <summary>How often the latest reading of a device is sent again when it has not changed. Default: 1 second.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The first wait before reconnecting to a device or to the server; it doubles after each further failure. Default: 1 second.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between reconnection attempts. Default: 30 seconds.</summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The devices this agent reads.</summary>
    public List<DeviceSettings> Devices { get; set; } = [];

    /// <summary>The agent name to use: <see cref="AgentName"/>, or the computer name when it is empty.</summary>
    public string EffectiveAgentName => string.IsNullOrWhiteSpace(AgentName) ? Environment.MachineName : AgentName.Trim();
}

/// <summary>One device: how to reach it (transport) and how to talk to it (protocol).</summary>
public sealed class DeviceSettings
{
    /// <summary>A unique id for the device, for example <c>scale-1</c>. The web application knows the device by this id.</summary>
    public string? Id { get; set; }

    /// <summary>How the device is reached.</summary>
    public TransportSettings? Transport { get; set; }

    /// <summary>How the device is read.</summary>
    public ProtocolSettings? Protocol { get; set; }
}

/// <summary>
/// The transport of a device. <see cref="Type"/> selects which of the other fields apply:
/// <c>serial</c> (PortName, BaudRate, DataBits, Parity, StopBits, Handshake, DtrEnable, RtsEnable),
/// <c>tcp</c> (Host, Port, ConnectTimeout, KeepAlive) or <c>simulator</c> (Simulator).
/// </summary>
public sealed class TransportSettings
{
    /// <summary>The transport type: <c>serial</c>, <c>tcp</c> or <c>simulator</c>.</summary>
    public string? Type { get; set; }

    /// <summary>Serial: the port name, for example <c>COM3</c> or <c>/dev/ttyUSB0</c>.</summary>
    public string? PortName { get; set; }

    /// <summary>Serial: baud rate. Default: 9600.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>Serial: data bits, 5 to 8. Default: 8.</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>Serial: parity (None, Odd, Even, Mark, Space). Default: None.</summary>
    public SerialParity Parity { get; set; } = SerialParity.None;

    /// <summary>Serial: stop bits (One, OnePointFive, Two). Default: One.</summary>
    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;

    /// <summary>Serial: flow control (None, XOnXOff, RequestToSend, RequestToSendXOnXOff). Default: None.</summary>
    public SerialHandshake Handshake { get; set; } = SerialHandshake.None;

    /// <summary>Serial: raise DTR when the port opens. Default: true.</summary>
    public bool DtrEnable { get; set; } = true;

    /// <summary>Serial: raise RTS when the port opens. Default: true.</summary>
    public bool RtsEnable { get; set; } = true;

    /// <summary>TCP: host name or IP address.</summary>
    public string? Host { get; set; }

    /// <summary>TCP: port.</summary>
    public int? Port { get; set; }

    /// <summary>TCP: connection timeout. Default: 5 seconds.</summary>
    public TimeSpan? ConnectTimeout { get; set; }

    /// <summary>TCP: keep-alive idle time; off when empty.</summary>
    public TimeSpan? KeepAlive { get; set; }

    /// <summary>Simulator: the simulated device. Default: an MT-SICS scale showing 0 g.</summary>
    public SimulatorSettings? Simulator { get; set; }
}

/// <summary>A simulated device, for trying the agent without hardware. Mirrors <see cref="SimulatorOptions"/>.</summary>
public sealed class SimulatorSettings
{
    /// <summary>The kind of device: MtSics, ToledoContinuous or Replay. Default: MtSics.</summary>
    public SimulatorKind Kind { get; set; } = SimulatorKind.MtSics;

    /// <summary>The weight shown. Its decimals are kept. Default: 0.</summary>
    public decimal Weight { get; set; }

    /// <summary>The unit. Default: g.</summary>
    public string Unit { get; set; } = "g";

    /// <summary>The weight state (Stable, Motion, Overload, Underload). Default: Stable.</summary>
    public WeightState State { get; set; } = WeightState.Stable;

    /// <summary>MT-SICS: the serial number answered to I4.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>The time between frames (Toledo, replay) or stream lines (MT-SICS SIR). Default: 50 ms.</summary>
    public TimeSpan? Interval { get; set; }

    /// <summary>Toledo: add a checksum byte.</summary>
    public bool Checksum { get; set; }

    /// <summary>Replay: the recording file.</summary>
    public string? RecordingPath { get; set; }

    /// <summary>Replay: OnRequest or Interval. Default: OnRequest.</summary>
    public ReplayMode ReplayMode { get; set; } = ReplayMode.OnRequest;

    /// <summary>Replay: start again after the last line. Default: true.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>Scenario: the first MT-SICS command of each session is answered with ES.</summary>
    public bool FirstCommandReturnsSyntaxError { get; set; }

    /// <summary>Scenario: never answer or send.</summary>
    public bool Silent { get; set; }

    /// <summary>Scenario: answer MT-SICS weight commands with busy.</summary>
    public bool Busy { get; set; }
}

/// <summary>
/// The protocol of a device. <see cref="Type"/> selects which of the other fields apply:
/// <c>mt-sics</c> (Command, PollInterval, ResponseTimeout, ZeroTimeout, RetriesOnError, MaxConsecutiveTimeouts, Terminator),
/// <c>toledo-continuous</c> (ChecksumEnabled, InactivityTimeout, MaxConsecutiveInvalidFrames) or
/// <c>generic-ascii</c> (Pattern and the other rule fields, RequestCommand for demand mode). Empty fields keep the
/// protocol's default.
/// </summary>
public sealed class ProtocolSettings
{
    /// <summary>The protocol type: <c>mt-sics</c>, <c>toledo-continuous</c> or <c>generic-ascii</c>.</summary>
    public string? Type { get; set; }

    /// <summary>MT-SICS: the read command, S, SI (default) or SIR. Any other command is rejected (decision V-28).</summary>
    public string? Command { get; set; }

    /// <summary>MT-SICS and generic ASCII demand mode: the time between two requests.</summary>
    public TimeSpan? PollInterval { get; set; }

    /// <summary>MT-SICS and generic ASCII demand mode: how long to wait for an answer.</summary>
    public TimeSpan? ResponseTimeout { get; set; }

    /// <summary>MT-SICS: how long to wait for the answer to the zero command <c>Z</c>.</summary>
    public TimeSpan? ZeroTimeout { get; set; }

    /// <summary>MT-SICS: how often a command is resent after ES or ET.</summary>
    public int? RetriesOnError { get; set; }

    /// <summary>MT-SICS and generic ASCII demand mode: timeouts in a row after which the link is considered dead.</summary>
    public int? MaxConsecutiveTimeouts { get; set; }

    /// <summary>MT-SICS and generic ASCII: the line terminator of the device output (CrLf, Lf, Cr, Any).</summary>
    public LineTerminator? Terminator { get; set; }

    /// <summary>Toledo: the indicator sends a checksum byte.</summary>
    public bool ChecksumEnabled { get; set; }

    /// <summary>Toledo and generic ASCII listen-only mode: silence after which the link is considered dead.</summary>
    public TimeSpan? InactivityTimeout { get; set; }

    /// <summary>Toledo: invalid frames in a row after which the link is considered dead.</summary>
    public int? MaxConsecutiveInvalidFrames { get; set; }

    /// <summary>Generic ASCII: the regular expression with a <c>weight</c> group (required).</summary>
    public string? Pattern { get; set; }

    /// <summary>Generic ASCII: decimal separator, "." or ",".</summary>
    public string? DecimalSeparator { get; set; }

    /// <summary>Generic ASCII: thousands separator.</summary>
    public string? ThousandsSeparator { get; set; }

    /// <summary>Generic ASCII: regex that marks a stable weight.</summary>
    public string? StablePattern { get; set; }

    /// <summary>Generic ASCII: regex that marks a weight in motion.</summary>
    public string? MotionPattern { get; set; }

    /// <summary>Generic ASCII: regex that marks an overload.</summary>
    public string? OverloadPattern { get; set; }

    /// <summary>Generic ASCII: regex that marks an underload.</summary>
    public string? UnderloadPattern { get; set; }

    /// <summary>Generic ASCII: regex that marks a net weight.</summary>
    public string? NetPattern { get; set; }

    /// <summary>Generic ASCII: the unit when the line has none.</summary>
    public string? DefaultUnit { get; set; }

    /// <summary>Generic ASCII: the read command for demand mode. Must only read; empty means listen-only.</summary>
    public string? RequestCommand { get; set; }

    /// <summary>Generic ASCII: the terminator appended to the request command (CR and LF only).</summary>
    public string? RequestTerminator { get; set; }

    /// <summary>Generic ASCII: the longest line accepted.</summary>
    public int? MaxLineLength { get; set; }
}
