using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// <see cref="IDeviceCommands"/> over the agent hub: invokes the agent that serves the device and waits for its result
/// (a SignalR client result), at most <see cref="AgentOptions.CommandTimeout"/>.
/// </summary>
public sealed partial class AgentDeviceCommands(
    IHubContext<AgentHub> hub,
    LiveWeightStore store,
    IOptionsMonitor<AgentOptions> options,
    ILogger<AgentDeviceCommands> logger) : IDeviceCommands
{
    /// <inheritdoc />
    public async Task<DeviceCommandResult> ZeroAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        if (!store.TryGetAgent(deviceId, out var connectionId, out var commands))
        {
            return Log(deviceId, new DeviceCommandResult(DeviceCommandOutcome.Unavailable, "No connected agent serves the device."));
        }

        if (!commands.Contains(DeviceCommandNames.Zero, StringComparer.Ordinal))
        {
            return Log(deviceId, new DeviceCommandResult(DeviceCommandOutcome.NotSupported, "The device does not support zeroing."));
        }

        var timeout = options.CurrentValue.CommandTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = new AgentOptions().CommandTimeout;
        }

        // The agent gets less time than we wait, so that its answer (even "too late, not sent") arrives before we give up.
        var agentTimeout = timeout - TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(1).Ticks, timeout.Ticks / 5));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var answer = await hub.Clients.Client(connectionId)
                .InvokeAsync<AgentCommandResult>(AgentHubContract.Zero, deviceId, agentTimeout, timeoutCts.Token)
                .ConfigureAwait(false);
            var result = answer is null || !Enum.IsDefined(answer.Result)
                ? new DeviceCommandResult(DeviceCommandOutcome.Unavailable, "The agent sent no valid answer.")
                : new DeviceCommandResult(answer.Result, answer.Detail);
            return Log(deviceId, result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Log(deviceId, new DeviceCommandResult(
                DeviceCommandOutcome.Unavailable, $"The agent did not answer within {timeout.TotalSeconds:0.###} s."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The agent disconnected during the call, or its handler failed.
            return Log(deviceId, new DeviceCommandResult(DeviceCommandOutcome.Unavailable, "The agent could not carry out the command: " + ex.Message));
        }
    }

    private DeviceCommandResult Log(string deviceId, DeviceCommandResult result)
    {
        LogZero(logger, deviceId, result.Result, result.Detail);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Zero requested for device {DeviceId}: {Result}. {Detail}")]
    private static partial void LogZero(ILogger logger, string deviceId, DeviceCommandOutcome result, string? detail);
}
