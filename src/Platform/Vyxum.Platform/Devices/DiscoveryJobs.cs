using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vyxum.Platform;

/// <summary>The state of a discovery job.</summary>
public enum DiscoveryJobStatus
{
    /// <summary>The agent is scanning.</summary>
    Running,

    /// <summary>The agent answered; the ports and candidates are final.</summary>
    Completed,

    /// <summary>The discovery failed; <see cref="DiscoveryJobView.Code"/> says why.</summary>
    Failed,
}

/// <summary>A discovery job as returned by <c>GET /api/devices/discover/{id}</c>.</summary>
/// <param name="Id">The job id.</param>
/// <param name="Status">Running, completed or failed.</param>
/// <param name="Code">For a failed job, a UI string key (<c>devices.AgentTimeout</c>, <c>devices.DiscoveryFailed</c>); otherwise null.</param>
/// <param name="AgentName">The agent that scans.</param>
/// <param name="StartedAt">When the job started.</param>
/// <param name="Ports">The ports and their states; the latest progress while running.</param>
/// <param name="Candidates">The devices found; empty until the job completes.</param>
public sealed record DiscoveryJobView(
    string Id,
    DiscoveryJobStatus Status,
    string? Code,
    string AgentName,
    DateTimeOffset StartedAt,
    IReadOnlyList<DiscoveryPortState> Ports,
    IReadOnlyList<DiscoveryCandidate> Candidates);

/// <summary>The outcome of <see cref="DiscoveryJobs.Start"/>.</summary>
/// <param name="Id">The new job id; null when no job was started.</param>
/// <param name="Code">Why no job was started (<c>devices.NoAgent</c>, <c>devices.DiscoveryRunning</c>); null when started.</param>
public sealed record DiscoveryStartResult(string? Id, string? Code);

/// <summary>
/// Device discovery jobs (decision V-40). A job asks one connected agent to scan (hub client method
/// <see cref="AgentHubContract.Discover"/>, with a result), keeps the progress the agent reports and, at the end, its
/// result. The platform passes the agent's findings through without reading them, so it knows no protocol or brand.
/// Jobs live in memory and are forgotten <see cref="Retention"/> after they started.
/// </summary>
/// <remarks>
/// Which agent scans: the one with the requested name; without a name, the default agent of the device settings
/// (<see cref="AgentDeviceAssignments"/>), or else the first registered agent. One job runs per agent at a time.
/// </remarks>
public sealed partial class DiscoveryJobs(
    IHubContext<AgentHub> hub,
    LiveWeightStore store,
    AgentDeviceAssignments assignments,
    IOptionsMonitor<AgentOptions> options,
    TimeProvider time,
    ILogger<DiscoveryJobs> logger)
{
    /// <summary>How long a job is kept after it started.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);

    /// <summary>
    /// Starts a discovery on an agent and returns the job id at once; the scan runs in the background. Returns a code
    /// instead when no agent is connected or the agent already runs a discovery.
    /// </summary>
    public DiscoveryStartResult Start(string? agentName, DiscoveryTcpTarget? tcp)
    {
        var agent = SelectAgent(agentName?.Trim());
        if (agent is not { } selected)
        {
            return new DiscoveryStartResult(null, DeviceStrings.Code("NoAgent"));
        }

        Job job;
        lock (_lock)
        {
            Purge();
            if (_jobs.Values.Any(j => j.Status == DiscoveryJobStatus.Running
                && string.Equals(j.AgentName, selected.AgentName, StringComparison.OrdinalIgnoreCase)))
            {
                return new DiscoveryStartResult(null, DeviceStrings.Code("DiscoveryRunning"));
            }

            job = new Job(Guid.NewGuid().ToString("N"), selected.ConnectionId, selected.AgentName, time.GetUtcNow());
            _jobs.Add(job.Id, job);
        }

        LogStarted(logger, job.Id, job.AgentName, tcp is null ? "serial ports" : $"{tcp.Host}:{tcp.Port}");
        _ = Task.Run(() => RunAsync(job, tcp), CancellationToken.None);
        return new DiscoveryStartResult(job.Id, null);
    }

    /// <summary>The job with the id, or null when it is unknown or was forgotten.</summary>
    public DiscoveryJobView? Get(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_lock)
        {
            Purge();
            return _jobs.TryGetValue(id, out var job) ? job.ToView() : null;
        }
    }

    /// <summary>Stores the progress reported by an agent. Ignored unless the job is running and belongs to that connection.</summary>
    public void ReportProgress(string connectionId, string discoveryId, IReadOnlyList<DiscoveryPortState> ports)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        if (discoveryId is null || ports is null)
        {
            return;
        }

        lock (_lock)
        {
            if (_jobs.TryGetValue(discoveryId, out var job) && job.ConnectionId == connectionId && job.Status == DiscoveryJobStatus.Running)
            {
                job.Ports = [.. ports.Where(p => p is not null && !string.IsNullOrWhiteSpace(p.PortName) && Enum.IsDefined(p.State))];
            }
        }
    }

    private (string ConnectionId, string AgentName)? SelectAgent(string? agentName)
    {
        var agents = store.GetAgents();
        if (!string.IsNullOrEmpty(agentName))
        {
            return agents.Where(a => string.Equals(a.AgentName, agentName, StringComparison.OrdinalIgnoreCase))
                .Select(a => ((string, string)?)a)
                .FirstOrDefault();
        }

        foreach (var connectionId in assignments.Connections)
        {
            foreach (var agent in agents)
            {
                if (agent.ConnectionId == connectionId)
                {
                    return agent;
                }
            }
        }

        return agents.Count > 0 ? agents[0] : null;
    }

    private async Task RunAsync(Job job, DiscoveryTcpTarget? tcp)
    {
        var timeout = options.CurrentValue.DiscoveryTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = new AgentOptions().DiscoveryTimeout;
        }

        using var timeoutCts = new CancellationTokenSource(timeout, time);
        try
        {
            var result = await hub.Clients.Client(job.ConnectionId)
                .InvokeAsync<DiscoveryResult>(AgentHubContract.Discover, new DiscoveryRequest(job.Id, tcp), timeoutCts.Token)
                .ConfigureAwait(false);
            if (result is null)
            {
                Finish(job, DiscoveryJobStatus.Failed, DeviceStrings.Code("DiscoveryFailed"), "The agent sent no result.");
                return;
            }

            lock (_lock)
            {
                job.Ports = [.. (result.Ports ?? []).Where(p => p is not null && Enum.IsDefined(p.State))];
                job.Candidates = [.. (result.Candidates ?? []).Where(c => c is not null)];
            }

            Finish(job, DiscoveryJobStatus.Completed, null, null);
        }
        catch (Exception) when (timeoutCts.IsCancellationRequested)
        {
            // SignalR reports a cancelled client result as a HubException ("Invocation canceled by the server").
            Finish(job, DiscoveryJobStatus.Failed, DeviceStrings.Code("AgentTimeout"), $"The agent did not answer within {timeout.TotalSeconds:0} s.");
        }
        catch (Exception ex)
        {
            // The agent disconnected during the scan, or the scan failed on the agent.
            Finish(job, DiscoveryJobStatus.Failed, DeviceStrings.Code("DiscoveryFailed"), ex.Message);
        }
    }

    private void Finish(Job job, DiscoveryJobStatus status, string? code, string? detail)
    {
        int candidates;
        lock (_lock)
        {
            job.Status = status;
            job.Code = code;
            candidates = job.Candidates.Count;
        }

        LogFinished(logger, job.Id, job.AgentName, status, candidates, detail);
    }

    /// <summary>Forgets jobs that started more than <see cref="Retention"/> ago and are no longer running. Call under the lock.</summary>
    private void Purge()
    {
        var now = time.GetUtcNow();
        foreach (var old in _jobs.Values.Where(j => j.Status != DiscoveryJobStatus.Running && now - j.StartedAt > Retention).ToList())
        {
            _jobs.Remove(old.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Device discovery {Id} started on agent {AgentName} ({Target}).")]
    private static partial void LogStarted(ILogger logger, string id, string agentName, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device discovery {Id} on agent {AgentName}: {Status}, {Candidates} device(s) found. {Detail}")]
    private static partial void LogFinished(ILogger logger, string id, string agentName, DiscoveryJobStatus status, int candidates, string? detail);

    private sealed class Job(string id, string connectionId, string agentName, DateTimeOffset startedAt)
    {
        public string Id { get; } = id;

        public string ConnectionId { get; } = connectionId;

        public string AgentName { get; } = agentName;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public DiscoveryJobStatus Status { get; set; } = DiscoveryJobStatus.Running;

        public string? Code { get; set; }

        public IReadOnlyList<DiscoveryPortState> Ports { get; set; } = [];

        public IReadOnlyList<DiscoveryCandidate> Candidates { get; set; } = [];

        public DiscoveryJobView ToView() => new(Id, Status, Code, AgentName, StartedAt, Ports, Candidates);
    }
}
