using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vyxum.Devices;

namespace Vyxum.Agent;

/// <summary>Runs the <see cref="DeviceRunner"/> of every configured device and feeds its readings to the <see cref="DeviceFleet"/>.</summary>
public sealed partial class DeviceWorker(DeviceFleet fleet, ILogger<DeviceWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();
        foreach (var device in fleet.Devices)
        {
            LogStarting(logger, device.Id, device.TransportDescription, device.ProtocolName);
            var runner = fleet.GetRunner(device.Id);

            // A transport may open synchronously (serial port), so each runner gets its own thread to start on.
            tasks.Add(Task.Run(() => runner.RunAsync(stoppingToken), CancellationToken.None));
            tasks.Add(Task.Run(() => PumpAsync(device.Id, runner), CancellationToken.None));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task PumpAsync(string deviceId, DeviceRunner runner)
    {
        // Ends when the runner stops and completes its reading channel.
        await foreach (var reading in runner.Readings.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            fleet.OnReading(deviceId, reading);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting device {DeviceId}: {Transport}, protocol {Protocol}.")]
    private static partial void LogStarting(ILogger logger, string deviceId, string transport, string protocol);
}
