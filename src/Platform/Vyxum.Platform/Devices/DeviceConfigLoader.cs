using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vyxum.Platform;

/// <summary>
/// Loads the device details from <c>plt_Devices</c> into <see cref="LiveWeightStore"/> when the host starts, so that
/// configured devices are listed before their agent connects. Registered after the database initializer.
/// </summary>
internal sealed partial class DeviceConfigLoader(IServiceScopeFactory scopes, ILogger<DeviceConfigLoader> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DeviceConfigService>().RefreshStoreAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Not fatal: the details are loaded again when an agent asks for its devices or the settings change.
            LogLoadFailed(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the device settings at startup.")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
