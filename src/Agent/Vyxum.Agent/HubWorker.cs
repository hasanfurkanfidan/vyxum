using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vyxum.Agent.Contract;
using Vyxum.Agent.Discovery;
using Vyxum.Devices;

namespace Vyxum.Agent;

/// <summary>Hooks for the connection to the web application, mainly for tests (for example an in-memory test server).</summary>
public sealed class AgentHubClientOptions
{
    /// <summary>Called after the agent has applied its own connection settings (URL, key header).</summary>
    public Action<HttpConnectionOptions>? ConfigureHttpConnection { get; set; }
}

/// <summary>
/// Keeps the connection to the agent hub of the web application and sends device readings and statuses over it.
/// </summary>
/// <remarks>
/// The first connect is retried forever with an increasing wait; a lost connection is restored by SignalR's automatic
/// reconnect with a policy that never gives up; and if the connection closes anyway, the first-connect loop takes over
/// again. After every (re)connect the agent registers and sends the status and the latest reading of every device.
/// A reading is sent when it differs from the last one sent, and the latest reading is sent again every heartbeat
/// interval. While disconnected nothing is queued: only the latest state per device is kept, and it is sent on reconnect.
/// </remarks>
public sealed partial class HubWorker : BackgroundService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private readonly DeviceFleet _fleet;
    private readonly DeviceDiscovery _discovery;
    private readonly RawPrinter _printer;
    private readonly AgentHubClientOptions _clientOptions;
    private readonly ILogger<HubWorker> _logger;
    private readonly TimeProvider _time;
    private readonly DeviceRunnerOptions _backoff;
    private readonly SemaphoreSlim _closed = new(0);
    private readonly Dictionary<string, SentState> _sent = new(StringComparer.Ordinal);
    private readonly string? _version;
    private int _resyncRequested;
    private int _fetchRequested;
    private bool _fetchUnsupportedLogged;
    private bool _sendFailureLogged;

    /// <summary>Creates the worker.</summary>
    public HubWorker(
        DeviceFleet fleet,
        DeviceDiscovery discovery,
        RawPrinter printer,
        IOptions<AgentHubClientOptions> clientOptions,
        ILogger<HubWorker> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(clientOptions);
        _fleet = fleet;
        _discovery = discovery;
        _printer = printer;
        _clientOptions = clientOptions.Value;
        _logger = logger;
        _time = timeProvider;
        _backoff = new DeviceRunnerOptions
        {
            InitialReconnectDelay = fleet.Settings.ReconnectDelay,
            MaxReconnectDelay = fleet.Settings.MaxReconnectDelay,
        };
        _version = typeof(HubWorker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
    }

    private string ServerUrl => _fleet.Settings.ServerUrl;

    /// <summary>The hub address for a server address.</summary>
    public static Uri GetHubUri(string serverUrl)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        return new Uri(new Uri(serverUrl.TrimEnd('/') + "/"), AgentHubContract.Path.TrimStart('/'));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = BuildConnection();
        await using (connection.ConfigureAwait(false))
        {
            // Called by the web application with a result (decision V-29): the operator pressed "Zero".
            connection.On<string, TimeSpan, AgentCommandResult>(
                AgentHubContract.Zero,
                (deviceId, timeout) => _fleet.ZeroAsync(deviceId, timeout, stoppingToken));

            // Called by the web application with a result (decision V-40): look for scales with read-only commands.
            connection.On<DiscoveryRequest, DiscoveryResult>(
                AgentHubContract.Discover,
                request => _discovery.DiscoverAsync(
                    request,
                    ports => connection.SendAsync(AgentHubContract.ReportDiscoveryProgress, request.DiscoveryId, ports, stoppingToken),
                    stoppingToken));

            // Called by the web application with a result: send a print job to a printer connected to this computer.
            connection.On<AgentPrintRequest, AgentPrintResult>(
                AgentHubContract.PrintRaw,
                request => _printer.PrintAsync(request, stoppingToken));

            if (!_fleet.UsesLocalDevices)
            {
                // The device settings changed in the web application (decision V-39).
                connection.On(AgentHubContract.DevicesChanged, () =>
                {
                    LogDevicesChanged(_logger);
                    RequestResync(fetchDevices: true);
                });
            }

            connection.Reconnecting += error =>
            {
                LogReconnecting(_logger, ServerUrl, error?.Message ?? "no error");
                return Task.CompletedTask;
            };
            connection.Reconnected += _ =>
            {
                LogReconnected(_logger, ServerUrl);
                RequestResync(fetchDevices: true);
                return Task.CompletedTask;
            };
            connection.Closed += error =>
            {
                if (!stoppingToken.IsCancellationRequested)
                {
                    LogClosed(_logger, ServerUrl, error?.Message ?? "no error");
                    _closed.Release();
                }

                return Task.CompletedTask;
            };

            LogStarting(_logger, ServerUrl, _fleet.Settings.EffectiveAgentName, string.IsNullOrEmpty(_fleet.Settings.Key) ? "no" : "an");
            if (_fleet.UsesLocalDevices)
            {
                LogLocalDevices(_logger, _fleet.Devices.Count);
            }
            else
            {
                LogServerDevices(_logger);
            }

            var sender = Task.Run(() => SendLoopAsync(connection, stoppingToken), CancellationToken.None);
            try
            {
                await SuperviseAsync(connection, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down.
            }

            try
            {
                await sender.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down.
            }
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _closed.Dispose();
        base.Dispose();
    }

    private HubConnection BuildConnection()
    {
        var key = _fleet.Settings.Key;
        return new HubConnectionBuilder()
            .WithUrl(GetHubUri(ServerUrl), options =>
            {
                if (!string.IsNullOrEmpty(key))
                {
                    // The .NET client sends headers on every transport, WebSockets included.
                    options.Headers[AgentHubContract.KeyHeader] = key;
                }

                _clientOptions.ConfigureHttpConnection?.Invoke(options);
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .WithAutomaticReconnect(new ForeverRetryPolicy(_backoff))
            .Build();
    }

    /// <summary>Connects, and connects again whenever the connection closes for good. Returns only by cancellation.</summary>
    private async Task SuperviseAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        var failures = 0;
        while (true)
        {
            try
            {
                await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failures++;
                var delay = _backoff.GetReconnectDelay(failures);
                LogConnectFailed(_logger, ServerUrl, ex.Message, delay);
                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            failures = 0;
            LogConnected(_logger, ServerUrl);
            RequestResync(fetchDevices: true);

            // Automatic reconnect handles short outages; this only wakes up when the connection is closed for good.
            await _closed.WaitAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(_fleet.Settings.ReconnectDelay, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private void RequestResync(bool fetchDevices = false)
    {
        if (fetchDevices && !_fleet.UsesLocalDevices)
        {
            Volatile.Write(ref _fetchRequested, 1);
        }

        Volatile.Write(ref _resyncRequested, 1);
        _fleet.Wake();
    }

    private async Task SendLoopAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        var interval = _fleet.Settings.HeartbeatInterval;
        var reader = _fleet.Updates;
        var lastHeartbeat = _time.GetTimestamp();
        while (true)
        {
            var remaining = interval - _time.GetElapsedTime(lastHeartbeat);
            if (remaining > TimeSpan.Zero)
            {
                await WaitForUpdatesAsync(reader.WaitToReadAsync, remaining, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var heartbeatDue = _time.GetElapsedTime(lastHeartbeat) >= interval;
            if (heartbeatDue)
            {
                lastHeartbeat = _time.GetTimestamp();
            }

            if (connection.State != HubConnectionState.Connected)
            {
                // Nothing is queued while disconnected; the latest state is sent after the reconnect.
                while (reader.TryRead(out _))
                {
                }

                continue;
            }

            if (Interlocked.Exchange(ref _resyncRequested, 0) != 0 && !await ResyncAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                Volatile.Write(ref _resyncRequested, 1);
                await Task.Delay(_fleet.Settings.ReconnectDelay, _time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var ok = true;
            while (ok && reader.TryRead(out var update))
            {
                ok = await ProcessAsync(connection, update, cancellationToken).ConfigureAwait(false);
            }

            if (ok && heartbeatDue)
            {
                ok = await HeartbeatAsync(connection, interval, cancellationToken).ConfigureAwait(false);
            }

            if (!ok)
            {
                // Something may have been lost; send the full state again once the connection works.
                Volatile.Write(ref _resyncRequested, 1);
            }
        }
    }

    private async Task WaitForUpdatesAsync(Func<CancellationToken, ValueTask<bool>> wait, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await wait(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Heartbeat time.
        }
    }

    private async Task<bool> ResyncAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        _sent.Clear();
        var fetch = Interlocked.Exchange(ref _fetchRequested, 0) != 0;
        AgentRegistration registration;
        try
        {
            // GetDevices needs a registered connection, so register first with the devices that run now.
            registration = await RegisterAsync(connection, cancellationToken).ConfigureAwait(false);
            if (fetch)
            {
                var changed = await FetchDevicesAsync(connection, cancellationToken).ConfigureAwait(false);
                fetch = false;
                if (changed)
                {
                    // The web application must know the devices that run now and the commands they support.
                    registration = await RegisterAsync(connection, cancellationToken).ConfigureAwait(false);
                }
            }

            var deviceIds = registration.DeviceIds;

            foreach (var deviceId in deviceIds)
            {
                if (_fleet.GetStatus(deviceId) is not { } status)
                {
                    continue;
                }

                await SendStatusAsync(connection, deviceId, status, cancellationToken).ConfigureAwait(false);
                if (status.Status == DeviceStatus.Connected && _fleet.GetLatestReading(deviceId) is { } reading)
                {
                    await SendReadingAsync(connection, deviceId, reading, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (fetch)
            {
                Volatile.Write(ref _fetchRequested, 1);
            }

            LogSendFailed(_logger, ex.Message);
            return false;
        }

        _sendFailureLogged = false;
        LogRegistered(_logger, registration.AgentName, registration.DeviceIds.Count);
        return true;
    }

    /// <summary>Registers the devices that run now.</summary>
    private async Task<AgentRegistration> RegisterAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        var deviceIds = _fleet.DeviceIds;
        var commands = deviceIds.ToDictionary(id => id, _fleet.GetSupportedCommands, StringComparer.Ordinal);
        IReadOnlyList<string> features = _printer.Enabled ? [AgentFeatures.RawPrint] : [];
        var registration = new AgentRegistration(_fleet.Settings.EffectiveAgentName, _version, deviceIds, commands, features);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        await connection.InvokeAsync(AgentHubContract.Register, registration, timeout.Token).ConfigureAwait(false);
        return registration;
    }

    /// <summary>
    /// Asks the web application for this agent's devices and applies them. Returns whether the devices changed. A web
    /// application without device settings (an older version) answers with an error; then nothing changes.
    /// </summary>
    private async Task<bool> FetchDevicesAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        AgentDeviceConfig[] devices;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);
            devices = await connection.InvokeAsync<AgentDeviceConfig[]>(AgentHubContract.GetDevices, timeout.Token).ConfigureAwait(false)
                ?? [];
        }
        catch (HubException ex)
        {
            if (!_fetchUnsupportedLogged)
            {
                _fetchUnsupportedLogged = true;
                LogFetchUnsupported(_logger, ex.Message);
            }

            return false;
        }

        _fetchUnsupportedLogged = false;
        return await _fleet.ApplyServerDevicesAsync(devices, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ProcessAsync(HubConnection connection, DeviceUpdate update, CancellationToken cancellationToken)
    {
        if (update.DeviceId is not { } deviceId)
        {
            return true;
        }

        try
        {
            if (_fleet.GetStatus(deviceId) is not { } current)
            {
                // The device left the fleet; the next registration tells the web application.
                return true;
            }

            if (update.Status is { } status)
            {
                if (!SameStatus(GetSent(deviceId).Status, status))
                {
                    await SendStatusAsync(connection, deviceId, status, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (update.Reading is { } reading
                && current.Status == DeviceStatus.Connected
                && !SameReading(GetSent(deviceId).Reading, reading))
            {
                // A reading that arrives after its device went down is stale: it would mark the device connected.
                await SendReadingAsync(connection, deviceId, reading, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogSendFailedOnce(ex);
            return false;
        }
    }

    private async Task<bool> HeartbeatAsync(HubConnection connection, TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var deviceId in _fleet.DeviceIds)
            {
                if (_fleet.GetStatus(deviceId) is not { } status)
                {
                    continue;
                }

                var sent = GetSent(deviceId);

                // Also repairs changes that were dropped from the queue.
                if (!SameStatus(sent.Status, status))
                {
                    await SendStatusAsync(connection, deviceId, status, cancellationToken).ConfigureAwait(false);
                }

                if (status.Status == DeviceStatus.Connected
                    && _fleet.GetLatestReading(deviceId) is { } reading
                    && (!SameReading(sent.Reading, reading) || _time.GetElapsedTime(sent.SentAt) >= interval * 0.9))
                {
                    await SendReadingAsync(connection, deviceId, reading, cancellationToken).ConfigureAwait(false);
                }
            }

            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogSendFailedOnce(ex);
            return false;
        }
    }

    private async Task SendStatusAsync(HubConnection connection, string deviceId, DeviceStatusInfo status, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        await connection.SendAsync(AgentHubContract.ReportStatus, deviceId, AgentDeviceStatus.From(status), timeout.Token).ConfigureAwait(false);
        GetSent(deviceId).Status = status;
    }

    private async Task SendReadingAsync(HubConnection connection, string deviceId, WeightReading reading, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        await connection.SendAsync(AgentHubContract.ReportReading, deviceId, AgentReading.From(reading), timeout.Token).ConfigureAwait(false);
        var sent = GetSent(deviceId);
        sent.Reading = reading;
        sent.SentAt = _time.GetTimestamp();
    }

    private SentState GetSent(string deviceId)
    {
        if (!_sent.TryGetValue(deviceId, out var sent))
        {
            sent = new SentState();
            _sent.Add(deviceId, sent);
        }

        return sent;
    }

    private void LogSendFailedOnce(Exception ex)
    {
        if (!_sendFailureLogged)
        {
            _sendFailureLogged = true;
            LogSendFailed(_logger, ex.Message);
        }
    }

    /// <summary>True when two statuses would look the same on the server (the timestamp does not count).</summary>
    internal static bool SameStatus(DeviceStatusInfo? sent, DeviceStatusInfo current) =>
        sent is not null && sent.Status == current.Status && sent.LastError == current.LastError && sent.RetryIn == current.RetryIn;

    /// <summary>True when two readings carry the same weight. The scale of the value counts: 1.25 and 1.250 differ.</summary>
    internal static bool SameReading(WeightReading? sent, WeightReading current) =>
        sent is not null
        && SameDecimal(sent.Value, current.Value)
        && sent.Unit == current.Unit
        && sent.State == current.State
        && SameDecimal(sent.Tare, current.Tare)
        && sent.IsNet == current.IsNet;

    private static bool SameDecimal(decimal? a, decimal? b) => a == b && a?.Scale == b?.Scale;

    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to {ServerUrl} as {AgentName} with {KeyState} agent key.")]
    private static partial void LogStarting(ILogger logger, string serverUrl, string agentName, string keyState);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not connect to {ServerUrl}: {Error} Retrying in {Delay}.")]
    private static partial void LogConnectFailed(ILogger logger, string serverUrl, string error, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to {ServerUrl}.")]
    private static partial void LogConnected(ILogger logger, string serverUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection to {ServerUrl} lost ({Error}); reconnecting.")]
    private static partial void LogReconnecting(ILogger logger, string serverUrl, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconnected to {ServerUrl}.")]
    private static partial void LogReconnected(ILogger logger, string serverUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection to {ServerUrl} closed ({Error}); connecting again.")]
    private static partial void LogClosed(ILogger logger, string serverUrl, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "The settings file lists {DeviceCount} device(s); they are used and the devices configured in the web application are ignored.")]
    private static partial void LogLocalDevices(ILogger logger, int deviceCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "The settings file lists no devices; the devices come from the web application.")]
    private static partial void LogServerDevices(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The device settings changed in the web application; fetching the devices again.")]
    private static partial void LogDevicesChanged(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The web application did not send devices: {Error} The agent runs without devices from the web application.")]
    private static partial void LogFetchUnsupported(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered as {AgentName} with {DeviceCount} device(s).")]
    private static partial void LogRegistered(ILogger logger, string agentName, int deviceCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending to the server failed: {Error} The full state is sent again when the connection works.")]
    private static partial void LogSendFailed(ILogger logger, string error);

    private sealed class SentState
    {
        public DeviceStatusInfo? Status { get; set; }

        public WeightReading? Reading { get; set; }

        public long SentAt { get; set; }
    }

    /// <summary>Retries immediately once, then with the device backoff (doubling up to the maximum), forever.</summary>
    private sealed class ForeverRetryPolicy(DeviceRunnerOptions backoff) : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.PreviousRetryCount == 0 ? TimeSpan.Zero : backoff.GetReconnectDelay((int)Math.Min(retryContext.PreviousRetryCount, 64));
    }
}
