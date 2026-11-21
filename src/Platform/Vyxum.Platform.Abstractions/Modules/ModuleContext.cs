using Microsoft.Extensions.Configuration;

namespace Vyxum.Platform.Abstractions;

/// <summary>What the platform offers a module while it registers its services.</summary>
public abstract class ModuleContext
{
    /// <summary>Application configuration.</summary>
    public abstract IConfiguration Configuration { get; }

    /// <summary>Capabilities provided by the loaded modules; use it for optional capabilities.</summary>
    public abstract ICapabilityRegistry Capabilities { get; }
}
