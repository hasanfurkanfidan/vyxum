using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// The hubs of modules that require a license, and their open connections. Hubs are found when the module's endpoints
/// are built (<see cref="LicenseGate.Apply"/>); connections are tracked by <see cref="LicenseHubFilter"/>. When the
/// license changes (<see cref="LicenseManager.Changed"/>), connections of modules that may no longer use their hubs are
/// aborted.
/// </summary>
internal sealed class LicensedHubs
{
    private readonly ConcurrentDictionary<Type, string> _modules = new();
    private readonly ConcurrentDictionary<string, (HubCallerContext Context, string ModuleId)> _connections = new(StringComparer.Ordinal);
    private readonly LicenseManager _licenses;

    public LicensedHubs(LicenseManager licenses)
    {
        _licenses = licenses;
        licenses.Changed += AbortDisallowed;
    }

    /// <summary>Records that <paramref name="hubType"/> belongs to the module <paramref name="moduleId"/>.</summary>
    public void Register(Type hubType, string moduleId) => _modules.TryAdd(hubType, moduleId);

    /// <summary>The module of <paramref name="hubType"/> when it requires a license; otherwise null.</summary>
    public string? ModuleOf(Type hubType) => _modules.GetValueOrDefault(hubType);

    /// <summary>
    /// The error code that denies the hub of <paramref name="moduleId"/>, or null when it may be used. A hub is a write
    /// channel, so only a licensed module or one in grace may connect or invoke: locked gives <c>license_required</c>,
    /// read-only gives <c>license_read_only</c>.
    /// </summary>
    public string? Deny(string moduleId) => _licenses.GetModuleStatus(moduleId) switch
    {
        var state when CapabilityRegistry.MayRun(state) => null,
        ModuleLicenseState.ReadOnly => LicenseGate.LicenseReadOnly,
        _ => LicenseGate.LicenseRequired,
    };

    public void Track(HubCallerContext context, string moduleId) => _connections[context.ConnectionId] = (context, moduleId);

    public void Untrack(HubCallerContext context) => _connections.TryRemove(context.ConnectionId, out _);

    /// <summary>Aborts the connections of modules that may no longer use their hubs.</summary>
    internal void AbortDisallowed()
    {
        foreach (var (_, (context, moduleId)) in _connections)
        {
            if (Deny(moduleId) is not null)
            {
                context.Abort();
            }
        }
    }
}

/// <summary>
/// Checks the license of the module that owns a hub on connect and on every invocation (registered for all hubs; hubs
/// of free modules and of the platform pass untouched). The license gate on endpoints only sees the HTTP request that
/// opens a connection: a WebSocket opened without negotiation is a GET (allowed in read-only mode), and a connection
/// opened while licensed would otherwise outlive the license.
/// </summary>
internal sealed class LicenseHubFilter(LicensedHubs hubs) : IHubFilter
{
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (hubs.ModuleOf(invocationContext.Hub.GetType()) is { } moduleId && hubs.Deny(moduleId) is { } code)
        {
            throw new HubException(code);
        }

        return next(invocationContext);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (hubs.ModuleOf(context.Hub.GetType()) is { } moduleId)
        {
            if (hubs.Deny(moduleId) is { } code)
            {
                throw new HubException(code);
            }

            hubs.Track(context.Context, moduleId);
        }

        await next(context);
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        hubs.Untrack(context.Context);
        await next(context, exception);
    }
}
