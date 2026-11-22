using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vyxum.Platform;

/// <summary>
/// The only writer of the latest seen license time (see <see cref="LicenseManager"/>). Every
/// <see cref="CheckInterval"/> it evaluates the license (so forward clock jumps are observed and time-based changes,
/// such as the end of the grace period, raise <see cref="LicenseManager.Changed"/> without a request), and every
/// <see cref="PersistInterval"/>, after an upload (<see cref="LicenseManager.RequestPersist"/>) and at shutdown it
/// persists the latest seen time.
/// </summary>
internal sealed partial class LicenseMaintenanceService(LicenseManager licenses, TimeProvider time, ILogger<LicenseMaintenanceService> logger)
    : BackgroundService
{
    /// <summary>How often the license is evaluated.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>How often the latest seen time is persisted.</summary>
    public static readonly TimeSpan PersistInterval = TimeSpan.FromHours(1);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // Best effort: the hourly write already keeps the loss small, and shutdown must not fail because of it.
        try
        {
            await licenses.PersistLastSeenAsync(cancellationToken);
        }
#pragma warning disable CA1031 // Any failure here (also of logging during shutdown) is ignored.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastPersist = time.GetTimestamp();
        var persist = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                licenses.GetSnapshot();
                if (persist || time.GetElapsedTime(lastPersist) >= PersistInterval)
                {
                    await licenses.PersistLastSeenAsync(stoppingToken);
                    lastPersist = time.GetTimestamp();
                }

                persist = await licenses.WaitForPersistRequestAsync(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogPersistFailed(logger, exception);
                persist = false;
                await Task.Delay(CheckInterval, time, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "License maintenance failed; it runs again shortly.")]
    private static partial void LogPersistFailed(ILogger logger, Exception exception);
}
