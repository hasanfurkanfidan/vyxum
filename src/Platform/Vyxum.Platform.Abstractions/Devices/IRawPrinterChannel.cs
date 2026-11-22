namespace Vyxum.Platform.Abstractions;

/// <summary>How a printer is connected to the computer of its agent.</summary>
public enum RawPrinterConnection
{
    /// <summary>A network printer, reached over TCP (usually port 9100).</summary>
    Network,

    /// <summary>A printer on a serial port of the agent's computer.</summary>
    Serial,
}

/// <summary>Where a print job goes.</summary>
/// <param name="AgentName">
/// The agent the printer is connected to, compared ignoring case. When null, the default agent takes the job: the agent
/// that serves the devices without an agent name, or the only connected agent.
/// </param>
/// <param name="Connection">How the printer is connected.</param>
/// <param name="Host">Network: host name or IP address.</param>
/// <param name="Port">Network: TCP port.</param>
/// <param name="PortName">Serial: the port name, for example <c>COM4</c>.</param>
/// <param name="BaudRate">Serial: baud rate. Data bits 8, no parity, one stop bit.</param>
public sealed record RawPrinterTarget(
    string? AgentName,
    RawPrinterConnection Connection,
    string? Host = null,
    int? Port = null,
    string? PortName = null,
    int? BaudRate = null);

/// <summary>A job for <see cref="IRawPrinterChannel.SendAsync"/>.</summary>
/// <param name="Target">Where the job goes.</param>
/// <param name="Data">The bytes to send, in the printer's own command language. The platform does not look into them.</param>
/// <param name="ReplyMaxBytes">
/// When above 0, the agent reads the printer's answer after sending, up to this many bytes (at most
/// <see cref="RawPrinterLimits.MaxReplyBytes"/>), for example the answer to a status query.
/// </param>
/// <param name="ReplyTimeout">How long the agent waits for the answer. Default: 2 seconds.</param>
/// <param name="ReplyEndByte">When set, the agent stops reading after this byte.</param>
public sealed record RawPrintRequest(
    RawPrinterTarget Target,
    ReadOnlyMemory<byte> Data,
    int ReplyMaxBytes = 0,
    TimeSpan? ReplyTimeout = null,
    byte? ReplyEndByte = null);

/// <summary>The outcome of a print job.</summary>
public enum RawPrintOutcome
{
    /// <summary>Every byte reached the printer connection. Whether the label came out is for the caller to check.</summary>
    Sent,

    /// <summary>Nothing was sent: no agent takes the job, the agent cannot print, or the job was too large.</summary>
    Unavailable,

    /// <summary>Nothing was sent: the agent does not allow this target (port not allowed, serial port used by a scale).</summary>
    Refused,

    /// <summary>Nothing was sent, or not all of it: the printer could not be reached or the connection broke.</summary>
    Failed,

    /// <summary>
    /// The agent got the job but its answer did not arrive (it disconnected or was too slow). The job may have been
    /// printed; do not send it again without asking.
    /// </summary>
    Uncertain,
}

/// <summary>The result of a print job.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Reply">The printer's answer when one was asked for; empty when it sent nothing in time.</param>
/// <param name="Detail">A short English explanation for logs; null when there is nothing to add. Not meant for display.</param>
public sealed record RawPrintResult(RawPrintOutcome Outcome, byte[]? Reply = null, string? Detail = null);

/// <summary>Limits of <see cref="IRawPrinterChannel"/>.</summary>
public static class RawPrinterLimits
{
    /// <summary>The largest job, in bytes.</summary>
    public const int MaxDataBytes = 4 * 1024 * 1024;

    /// <summary>The largest answer read from a printer, in bytes.</summary>
    public const int MaxReplyBytes = 4096;
}

/// <summary>
/// Sends bytes to a printer through the agent the printer is connected to. The platform knows no printer brand or
/// command language: it carries the bytes and, when asked, the printer's answer. Each call sends the job at most once.
/// </summary>
public interface IRawPrinterChannel
{
    /// <summary>The names of the connected agents that can print.</summary>
    IReadOnlyList<string> GetPrintingAgents();

    /// <summary>Sends a job. Never throws for agent or printer problems.</summary>
    Task<RawPrintResult> SendAsync(RawPrintRequest request, CancellationToken cancellationToken = default);
}
