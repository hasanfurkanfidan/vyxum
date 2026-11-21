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
}
