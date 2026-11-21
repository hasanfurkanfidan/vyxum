namespace Vyxum.Platform.Abstractions;

/// <summary>Answers which capabilities are provided by the loaded modules.</summary>
public interface ICapabilityRegistry
{
    /// <summary>True when some loaded module provides <paramref name="capability"/>.</summary>
    bool IsAvailable(Capability capability);
}
