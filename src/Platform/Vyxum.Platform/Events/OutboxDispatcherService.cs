using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vyxum.Platform;

/// <summary>Runs <see cref="OutboxDispatcher.DispatchPendingAsync"/> every <see cref="OutboxOptions.PollingInterval"/>.</summary>
internal sealed partial class OutboxDispatcherService(
    OutboxDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.DispatchPendingAsync(stoppingToken);
                await Task.Delay(options.Value.PollingInterval, time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogDispatchFailed(logger, exception);
                await Task.Delay(options.Value.PollingInterval, time, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatch failed; retrying at the next poll.")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);
}
