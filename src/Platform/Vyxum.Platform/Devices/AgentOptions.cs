namespace Vyxum.Platform;

/// <summary>Agent connection settings, bound from the <c>Vyxum:Agents</c> configuration section.</summary>
public sealed class AgentOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Vyxum:Agents";

    /// <summary>
    /// The key agents must send. When empty, only connections from this computer are accepted;
    /// when set, agents on any address are accepted with the right key (decision V-26).
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// How long a device command (such as zero) may take, including the round trip to the agent. After it the result
    /// is <see cref="Abstractions.DeviceCommandOutcome.Unavailable"/>. Default: 5 seconds.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a device discovery may take, including the round trip to the agent. After it the job fails with
    /// <c>devices.AgentTimeout</c>. Default: 60 seconds.
    /// </summary>
    public TimeSpan DiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a print job may take, including the round trip to the agent. After it the result is
    /// <see cref="Abstractions.RawPrintOutcome.Uncertain"/>. Default: 30 seconds.
    /// </summary>
    public TimeSpan PrintTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
