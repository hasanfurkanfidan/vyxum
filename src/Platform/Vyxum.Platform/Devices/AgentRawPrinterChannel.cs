using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// <see cref="IRawPrinterChannel"/> over the agent hub: invokes the agent the printer is connected to and waits for its
/// result (a SignalR client result), at most <see cref="AgentOptions.PrintTimeout"/>.
/// </summary>
public sealed partial class AgentRawPrinterChannel(
    IHubContext<AgentHub> hub,
    LiveWeightStore store,
    AgentDeviceAssignments assignments,
    IOptionsMonitor<AgentOptions> options,
    ILogger<AgentRawPrinterChannel> logger) : IRawPrinterChannel
{
    private static readonly TimeSpan DefaultReplyTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxReplyTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public IReadOnlyList<string> GetPrintingAgents() =>
        [.. PrintingAgents().Select(a => a.AgentName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <inheritdoc />
    public async Task<RawPrintResult> SendAsync(RawPrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);

        if (Validate(request) is { } invalid)
        {
            return Log(request, null, new RawPrintResult(RawPrintOutcome.Unavailable, Detail: invalid));
        }

        if (FindAgent(request.Target.AgentName) is not { } connectionId)
        {
            var detail = request.Target.AgentName is null
                ? "No connected agent can print, or several can and none is the default; name the agent."
                : $"No connected agent named '{request.Target.AgentName}' can print.";
            return Log(request, null, new RawPrintResult(RawPrintOutcome.Unavailable, Detail: detail));
        }

        var timeout = options.CurrentValue.PrintTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = new AgentOptions().PrintTimeout;
        }

        // The agent gets less time than we wait, so that its answer arrives before we give up.
        var agentTimeout = timeout - TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(2).Ticks, timeout.Ticks / 5));
        var replyTimeout = request.ReplyTimeout is { } t && t > TimeSpan.Zero ? (t > MaxReplyTimeout ? MaxReplyTimeout : t) : DefaultReplyTimeout;
        var target = request.Target;
        var job = new AgentPrintRequest(
            target.Connection,
            target.Host?.Trim(),
            target.Port,
            target.PortName?.Trim(),
            target.BaudRate,
            request.Data.ToArray(),
            request.ReplyMaxBytes,
            replyTimeout,
            request.ReplyEndByte,
            agentTimeout);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var answer = await hub.Clients.Client(connectionId)
                .InvokeAsync<AgentPrintResult>(AgentHubContract.PrintRaw, job, timeoutCts.Token)
                .ConfigureAwait(false);
            var result = answer is null || !Enum.IsDefined(answer.Outcome) || answer.Outcome == RawPrintOutcome.Uncertain
                ? new RawPrintResult(RawPrintOutcome.Uncertain, Detail: "The agent sent no valid answer.")
                : new RawPrintResult(answer.Outcome, Limit(answer.Reply, request.ReplyMaxBytes), answer.Detail);
            return Log(request, connectionId, result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Log(request, connectionId, new RawPrintResult(
                RawPrintOutcome.Uncertain, Detail: $"The agent did not answer within {timeout.TotalSeconds:0.###} s."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The agent disconnected during the call, or its handler failed: the job may have reached the printer.
            return Log(request, connectionId, new RawPrintResult(
                RawPrintOutcome.Uncertain, Detail: "The agent could not finish the job: " + ex.Message));
        }
    }

    private static string? Validate(RawPrintRequest request)
    {
        var target = request.Target;
        if (request.Data.IsEmpty)
        {
            return "The job has no data.";
        }

        if (request.Data.Length > RawPrinterLimits.MaxDataBytes)
        {
            return $"The job is larger than {RawPrinterLimits.MaxDataBytes} bytes.";
        }

        if (request.ReplyMaxBytes is < 0 or > RawPrinterLimits.MaxReplyBytes)
        {
            return $"The answer limit must be between 0 and {RawPrinterLimits.MaxReplyBytes} bytes.";
        }

        return target.Connection switch
        {
            RawPrinterConnection.Network when string.IsNullOrWhiteSpace(target.Host) => "A network printer needs a host.",
            RawPrinterConnection.Network when target.Port is not (>= 1 and <= 65535) => "A network printer needs a TCP port between 1 and 65535.",
            RawPrinterConnection.Serial when string.IsNullOrWhiteSpace(target.PortName) => "A serial printer needs a port name.",
            RawPrinterConnection.Serial when target.BaudRate is not (>= 300 and <= 921600) => "A serial printer needs a baud rate between 300 and 921600.",
            RawPrinterConnection.Network or RawPrinterConnection.Serial => null,
            _ => "Unknown printer connection.",
        };
    }

    private static byte[] Limit(byte[]? reply, int max) =>
        reply is null || max <= 0 ? [] : reply.Length > max ? reply[..max] : reply;

    private IEnumerable<(string ConnectionId, string AgentName)> PrintingAgents() =>
        store.GetAgents().Where(a => store.HasFeature(a.ConnectionId, AgentFeatures.RawPrint));

    private string? FindAgent(string? agentName)
    {
        var printing = PrintingAgents().ToList();
        if (agentName is not null)
        {
            // The newest connection of that name, like a restarted agent whose old connection has not timed out yet.
            return printing.LastOrDefault(a => string.Equals(a.AgentName, agentName.Trim(), StringComparison.OrdinalIgnoreCase)).ConnectionId;
        }

        var connections = printing.Select(a => a.ConnectionId).ToHashSet(StringComparer.Ordinal);
        if (assignments.Connections.FirstOrDefault() is { } defaultAgent && connections.Contains(defaultAgent))
        {
            return defaultAgent;
        }

        return printing.Count == 1 ? printing[0].ConnectionId : null;
    }

    private RawPrintResult Log(RawPrintRequest request, string? connectionId, RawPrintResult result)
    {
        var target = request.Target;
        var where = target.Connection == RawPrinterConnection.Network ? $"{target.Host}:{target.Port}" : target.PortName;
        var agent = connectionId is null ? target.AgentName : store.GetAgentName(connectionId) ?? target.AgentName;
        LogPrint(logger, request.Data.Length, where, agent ?? "(default)", result.Outcome, result.Detail);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Print job of {Bytes} byte(s) to {Target} through agent {Agent}: {Outcome}. {Detail}")]
    private static partial void LogPrint(ILogger logger, int bytes, string? target, string agent, RawPrintOutcome outcome, string? detail);
}
