using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// The hub Vyxum Agent connects to (decision V-13). Method names and payloads are described by
/// <see cref="AgentHubContract"/>.
/// </summary>
public sealed partial class AgentHub(
    LiveWeightStore store,
    AgentDeviceAssignments assignments,
    DiscoveryJobs discoveries,
    IServiceProvider services,
    ILogger<AgentHub> logger) : Hub
{
    /// <summary>Registers the agent and the devices it serves.</summary>
    public void Register(AgentRegistration registration)
    {
        if (registration is null || string.IsNullOrWhiteSpace(registration.AgentName))
        {
            throw new HubException("Registration must name the agent.");
        }

        var deviceIds = registration.DeviceIds ?? [];
        if (deviceIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new HubException("Device ids must not be empty.");
        }

        store.RegisterAgent(Context.ConnectionId, registration.AgentName, deviceIds, registration.DeviceCommands);
        LogRegistered(logger, registration.AgentName, registration.Version, deviceIds.Count);
    }

    /// <summary>
    /// Returns the devices from the device settings that the calling agent serves (decision V-39). The agent must have
    /// registered on this connection first.
    /// </summary>
    public async Task<IReadOnlyList<AgentDeviceConfig>> GetDevices()
    {
        var agentName = store.GetAgentName(Context.ConnectionId)
            ?? throw new HubException("Register before asking for devices.");
        var isDefault = assignments.Join(Context.ConnectionId, agentName);

        // A scoped service: the hub instance lives in the invocation's scope.
        var configs = services.GetService<DeviceConfigService>()
            ?? throw new HubException("Device settings are not available.");
        var devices = await configs.GetAgentDevicesAsync(agentName, isDefault, Context.ConnectionAborted);
        LogDevicesSent(logger, agentName, devices.Count, isDefault);
        return devices;
    }

    /// <summary>Stores a reading of a device.</summary>
    public void ReportReading(string deviceId, AgentReading reading)
    {
        RequireDeviceId(deviceId);
        if (reading is null || !Enum.IsDefined(reading.State))
        {
            throw new HubException("The reading is missing or has an unknown state.");
        }

        store.ReportReading(Context.ConnectionId, deviceId, reading);
    }

    /// <summary>Stores a status change of a device.</summary>
    public void ReportStatus(string deviceId, AgentDeviceStatus status)
    {
        RequireDeviceId(deviceId);
        if (status is null || !Enum.IsDefined(status.Status))
        {
            throw new HubException("The status is missing or unknown.");
        }

        store.ReportStatus(Context.ConnectionId, deviceId, status);
        LogStatus(logger, deviceId, status.Status, status.LastError);
    }

    /// <summary>Stores the progress of a device discovery this agent runs (decision V-40).</summary>
    public void ReportDiscoveryProgress(string discoveryId, IReadOnlyList<DiscoveryPortState> ports)
    {
        if (string.IsNullOrWhiteSpace(discoveryId))
        {
            throw new HubException("The discovery id must not be empty.");
        }

        discoveries.ReportProgress(Context.ConnectionId, discoveryId, ports ?? []);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var devices = store.DisconnectAgent(Context.ConnectionId);
        LogDisconnected(logger, Context.ConnectionId, devices);
        if (assignments.Leave(Context.ConnectionId) is { } newDefault)
        {
            // The devices without an agent name move to the next agent in line.
            await Clients.Client(newDefault).SendAsync(AgentHubContract.DevicesChanged);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private static void RequireDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new HubException("The device id must not be empty.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentName} {Version} registered with {DeviceCount} device(s).")]
    private static partial void LogRegistered(ILogger logger, string agentName, string? version, int deviceCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentName} gets {DeviceCount} device(s) from the device settings (default agent: {IsDefault}).")]
    private static partial void LogDevicesSent(ILogger logger, string agentName, int deviceCount, bool isDefault);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Device {DeviceId} is {Status}. {LastError}")]
    private static partial void LogStatus(ILogger logger, string deviceId, DeviceConnectionState status, string? lastError);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent connection {ConnectionId} closed; {DeviceCount} device(s) marked disconnected.")]
    private static partial void LogDisconnected(ILogger logger, string connectionId, int deviceCount);
}
