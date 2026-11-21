using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vyxum.Platform;

/// <summary>Restarts the web host, for example after the database was changed in the setup wizard.</summary>
public interface IHostRestarter
{
    /// <summary>
    /// Stops the host so that it starts again. Returns false when the host cannot restart itself
    /// (not running as a Windows service); a manual restart is then needed.
    /// </summary>
    bool Restart();
}

/// <summary>Settings of the default <see cref="IHostRestarter"/>.</summary>
public sealed class HostRestartOptions
{
    /// <summary>The exit code the process ends with when it restarts itself.</summary>
    public const int RestartExitCode = 3;

    /// <summary>
    /// True when the host runs as a Windows service whose recovery settings start it again after a failure.
    /// The host then restarts by exiting with <see cref="RestartExitCode"/>.
    /// </summary>
    public bool ExitForServiceRecovery { get; set; }
}

/// <summary>
/// Stops the application and ends the process with <see cref="HostRestartOptions.RestartExitCode"/>.
/// The process exits after the host has stopped but before the service reports "stopped" to Windows, so the
/// service control manager treats it as a failure and its recovery settings start the service again.
/// </summary>
internal sealed partial class ServiceRecoveryRestarter(
    IHostApplicationLifetime lifetime,
    IOptions<HostRestartOptions> options,
    ILogger<ServiceRecoveryRestarter> logger) : IHostRestarter
{
    private int _requested;

    public bool Restart()
    {
        if (!options.Value.ExitForServiceRecovery)
        {
            LogManualRestart(logger);
            return false;
        }

        if (Interlocked.Exchange(ref _requested, 1) == 1)
        {
            return true;
        }

        LogRestarting(logger, HostRestartOptions.RestartExitCode);
        Environment.ExitCode = HostRestartOptions.RestartExitCode;
        lifetime.ApplicationStopped.Register(() => Environment.Exit(HostRestartOptions.RestartExitCode));
        lifetime.StopApplication();
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The settings were changed and Vyxum must be restarted. It does not run as a Windows service, so restart it manually.")]
    private static partial void LogManualRestart(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The settings were changed. Stopping with exit code {ExitCode} so that the Windows service restarts.")]
    private static partial void LogRestarting(ILogger logger, int exitCode);
}
