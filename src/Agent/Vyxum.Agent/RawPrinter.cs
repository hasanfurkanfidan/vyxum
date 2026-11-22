using Microsoft.Extensions.Logging;
using Vyxum.Agent.Configuration;
using Vyxum.Agent.Contract;
using Vyxum.Devices;
using Vyxum.Devices.Transports;

namespace Vyxum.Agent;

/// <summary>
/// Carries out print jobs from the web application: opens the printer connection, writes the bytes once, reads the
/// answer when one is asked for, and closes the connection. It does not look into the bytes. One job runs at a time.
/// </summary>
/// <remarks>
/// Guards: network printers only on the allowed TCP ports (default 9100), and never a serial port that a scale of this
/// agent uses. A job is never retried here; the caller decides what to do after a failure.
/// </remarks>
public sealed partial class RawPrinter : IDisposable
{
    private readonly DeviceFleet _fleet;
    private readonly ILogger<RawPrinter> _logger;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>Creates the printer service.</summary>
    public RawPrinter(DeviceFleet fleet, ILogger<RawPrinter> logger)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        _fleet = fleet;
        _logger = logger;
    }

    /// <summary>Whether this agent takes print jobs.</summary>
    public bool Enabled => _fleet.Settings.Printing.Enabled;

    /// <summary>Carries out a job. Never throws for printer problems.</summary>
    public async Task<AgentPrintResult> PrintAsync(AgentPrintRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = _fleet.Settings.Printing;
        var target = request.Connection == AgentPrinterConnection.Network ? $"{request.Host}:{request.Port}" : request.PortName ?? "?";

        if (!settings.Enabled)
        {
            return Log(target, request, new AgentPrintResult(AgentPrintOutcome.Unavailable, Detail: "Printing is turned off on this agent."));
        }

        if (Check(request, settings) is { } refusal)
        {
            return Log(target, request, refusal);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout > TimeSpan.Zero ? request.Timeout : TimeSpan.FromSeconds(25));
        try
        {
            await _oneAtATime.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Log(target, request, new AgentPrintResult(AgentPrintOutcome.Unavailable, Detail: "Another print job did not finish in time; this one was not sent."));
        }

        try
        {
            return Log(target, request, await SendAsync(request, settings, timeout.Token).ConfigureAwait(false));
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _oneAtATime.Dispose();

    private AgentPrintResult? Check(AgentPrintRequest request, PrintingSettings settings)
    {
        if (request.Data is not { Length: > 0 })
        {
            return new AgentPrintResult(AgentPrintOutcome.Unavailable, Detail: "The job has no data.");
        }

        switch (request.Connection)
        {
            case AgentPrinterConnection.Network:
                if (string.IsNullOrWhiteSpace(request.Host) || request.Port is not { } port)
                {
                    return new AgentPrintResult(AgentPrintOutcome.Unavailable, Detail: "A network printer needs a host and a port.");
                }

                if (!settings.EffectiveTcpPorts.Contains(port))
                {
                    return new AgentPrintResult(
                        AgentPrintOutcome.Refused,
                        Detail: $"TCP port {port} is not allowed for printers on this agent (allowed: {string.Join(", ", settings.EffectiveTcpPorts)}).");
                }

                return null;

            case AgentPrinterConnection.Serial:
                if (string.IsNullOrWhiteSpace(request.PortName) || request.BaudRate is not > 0)
                {
                    return new AgentPrintResult(AgentPrintOutcome.Unavailable, Detail: "A serial printer needs a port name and a baud rate.");
                }

                if (_fleet.GetPortUsers().TryGetValue(request.PortName.Trim(), out var deviceId))
                {
                    return new AgentPrintResult(AgentPrintOutcome.Refused, Detail: $"Serial port {request.PortName} is used by device {deviceId}.");
                }

                return null;

            default:
                return new AgentPrintResult(AgentPrintOutcome.Unavailable, Detail: "Unknown printer connection.");
        }
    }

    private static ITransport CreateTransport(AgentPrintRequest request, PrintingSettings settings, TimeSpan timeout) =>
        request.Connection == AgentPrinterConnection.Network
            ? new TcpTransport(new TcpTransportOptions
            {
                Host = request.Host!.Trim(),
                Port = request.Port!.Value,
                ConnectTimeout = settings.ConnectTimeout > TimeSpan.Zero ? settings.ConnectTimeout : TimeSpan.FromSeconds(5),
            })
            : new SerialTransport(new SerialTransportOptions
            {
                PortName = request.PortName!.Trim(),
                BaudRate = request.BaudRate!.Value,

                // A large label at a low baud rate takes long; the job timeout is the limit.
                WriteTimeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(25),
            });

    private static async Task<AgentPrintResult> SendAsync(AgentPrintRequest request, PrintingSettings settings, CancellationToken cancellationToken)
    {
        await using var transport = CreateTransport(request, settings, request.Timeout);
        try
        {
            await transport.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new AgentPrintResult(AgentPrintOutcome.Failed, Detail: $"Could not open {transport.Description}: {ex.Message}");
        }

        try
        {
            transport.DiscardInput();
            await transport.WriteAsync(request.Data, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new AgentPrintResult(AgentPrintOutcome.Failed, Detail: $"Writing to {transport.Description} failed: {ex.Message}");
        }

        var reply = request.ReplyMaxBytes > 0
            ? await ReadReplyAsync(transport, request, cancellationToken).ConfigureAwait(false)
            : null;
        return new AgentPrintResult(AgentPrintOutcome.Sent, reply);
    }

    private static async Task<byte[]> ReadReplyAsync(ITransport transport, AgentPrintRequest request, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(request.ReplyMaxBytes, 4096)];
        var count = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.ReplyTimeout > TimeSpan.Zero ? request.ReplyTimeout : TimeSpan.FromSeconds(2));
        try
        {
            while (count < buffer.Length)
            {
                var read = await transport.ReadAsync(buffer.AsMemory(count), timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var end = request.ReplyEndByte is { } endByte ? Array.IndexOf(buffer, endByte, count, read) : -1;
                count += read;
                if (end >= 0)
                {
                    count = end + 1;
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            // The printer said nothing more in time, or closed the connection: keep what arrived.
        }

        return buffer[..count];
    }

    private AgentPrintResult Log(string target, AgentPrintRequest request, AgentPrintResult result)
    {
        LogJob(_logger, request.Data?.Length ?? 0, target, result.Outcome, result.Reply?.Length ?? 0, result.Detail);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Print job of {Bytes} byte(s) to {Target}: {Outcome}, {ReplyBytes} byte(s) of answer. {Detail}")]
    private static partial void LogJob(ILogger logger, int bytes, string target, AgentPrintOutcome outcome, int replyBytes, string? detail);
}
