using Microsoft.Extensions.Configuration;

namespace Vyxum.Platform.Abstractions;

/// <summary>What the platform offers a module while it registers its services.</summary>
public abstract class ModuleContext
{
    /// <summary>Application configuration.</summary>
    public abstract IConfiguration Configuration { get; }

    /// <summary>Capabilities provided by the loaded modules; use it for optional capabilities.</summary>
    public abstract ICapabilityRegistry Capabilities { get; }

    /// <summary>
    /// Registers the module's DbContext (scoped) with the configured database provider.
    /// A module owns at most one DbContext; the platform creates its tables at startup.
    /// </summary>
    public abstract void AddDbContext<TContext>()
        where TContext : ModuleDbContext;
}
