namespace Vyxum.Platform;

/// <summary>The validated set of modules loaded by the application.</summary>
/// <param name="Modules">Loaded modules, ordered by id.</param>
public sealed record ModuleCatalog(IReadOnlyList<ModuleDescriptor> Modules);
