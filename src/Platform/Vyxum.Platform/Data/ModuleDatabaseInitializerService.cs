using Microsoft.Extensions.Hosting;

namespace Vyxum.Platform;

/// <summary>Runs <see cref="IModuleDatabaseInitializer"/> when the host starts.</summary>
internal sealed class ModuleDatabaseInitializerService(IModuleDatabaseInitializer initializer) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => initializer.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
