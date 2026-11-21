namespace Vyxum.Platform;

/// <summary>
/// Which connected agent serves which device from <c>plt_Devices</c> (decision V-39).
/// </summary>
/// <remarks>
/// <para>An agent takes part once it asks for its devices (hub method <c>GetDevices</c>); agents that use devices from
/// their own settings file never ask and never get devices from the web.</para>
/// <para>The rule: a device whose <c>agentName</c> is set is served by every taking-part agent with that name (compared
/// ignoring case, like Windows computer names). A device without <c>agentName</c> is served by the <em>default
/// agent</em>: the taking-part agent that asked first and is still connected. On a single computer there is exactly one
/// agent, so it serves every device. When the default agent disconnects, the next one in line becomes the default and
/// is told to fetch its devices again.</para>
/// </remarks>
public sealed class AgentDeviceAssignments
{
    private readonly Lock _lock = new();
    private readonly List<string> _order = [];
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    /// <summary>The connections of the taking-part agents, default agent first.</summary>
    public IReadOnlyList<string> Connections
    {
        get
        {
            lock (_lock)
            {
                return [.. _order];
            }
        }
    }

    /// <summary>Adds the connection (keeping its place when already added) and returns whether it is the default agent.</summary>
    public bool Join(string connectionId, string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        lock (_lock)
        {
            if (!_names.ContainsKey(connectionId))
            {
                _order.Add(connectionId);
            }

            _names[connectionId] = agentName;
            return _order[0] == connectionId;
        }
    }

    /// <summary>
    /// Removes the connection. Returns the connection of the new default agent when the default changed to another
    /// agent; otherwise null.
    /// </summary>
    public string? Leave(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        lock (_lock)
        {
            if (!_names.Remove(connectionId))
            {
                return null;
            }

            var wasDefault = _order[0] == connectionId;
            _order.Remove(connectionId);
            return wasDefault && _order.Count > 0 ? _order[0] : null;
        }
    }

    /// <summary>True when a device assigned to <paramref name="deviceAgentName"/> is served by the given agent.</summary>
    public static bool Serves(string? deviceAgentName, string agentName, bool isDefault) =>
        deviceAgentName is null ? isDefault : string.Equals(deviceAgentName, agentName, StringComparison.OrdinalIgnoreCase);
}
