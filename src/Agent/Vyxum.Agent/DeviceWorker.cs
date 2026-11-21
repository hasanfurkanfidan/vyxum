using Microsoft.Extensions.Hosting;

namespace Vyxum.Agent;

/// <summary>Runs the devices of the <see cref="DeviceFleet"/> until the agent stops.</summary>
public sealed class DeviceWorker(DeviceFleet fleet) : BackgroundService
{
    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => fleet.RunAsync(stoppingToken);
}
