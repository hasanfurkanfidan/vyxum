namespace Vyxum.Platform.Abstractions;

/// <summary>Answers which capabilities are provided by the loaded modules.</summary>
/// <remarks>
/// The registry a module gets from dependency injection follows the license: a capability of a module that requires a
/// license counts only while that module is licensed or in its grace period. The answer can change while the
/// application runs, so ask when it matters (per request or operation), not once at startup.
/// </remarks>
public interface ICapabilityRegistry
{
    /// <summary>
    /// True when some loaded module provides <paramref name="capability"/> and may run now (it is free, or licensed or
    /// in its grace period).
    /// </summary>
    bool IsAvailable(Capability capability);
}
