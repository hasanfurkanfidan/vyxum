using System.Reflection;

namespace Vyxum.Platform.Abstractions;

/// <summary>Gives an integration event its stable wire name and version.</summary>
/// <param name="name">Event name: lowercase, dot-separated kebab-case segments, e.g. <c>samples.order-placed</c>.</param>
/// <param name="version">Event version, starting at 1.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class IntegrationEventAttribute(string name, int version) : Attribute
{
    /// <summary>Event name.</summary>
    public string Name { get; } = name;

    /// <summary>Event version.</summary>
    public int Version { get; } = version;

    /// <summary>Returns the attribute declared on <paramref name="eventType"/>, or null.</summary>
    public static IntegrationEventAttribute? Find(Type eventType) =>
        eventType.GetCustomAttribute<IntegrationEventAttribute>(inherit: false);

    /// <inheritdoc />
    public override string ToString() => $"{Name} v{Version}";
}
