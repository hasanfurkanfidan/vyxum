using Microsoft.Extensions.Configuration;

namespace Vyxum.Platform.Abstractions;

/// <summary>What the platform offers a module while it registers its services.</summary>
public abstract class ModuleContext
{
    /// <summary>Application configuration.</summary>
    public abstract IConfiguration Configuration { get; }

    /// <summary>
    /// Capabilities provided by the installed modules, while services are registered. It does not follow the license;
    /// at run time check optional capabilities with the <see cref="ICapabilityRegistry"/> from dependency injection.
    /// </summary>
    public abstract ICapabilityRegistry Capabilities { get; }

    /// <summary>
    /// Registers the module's DbContext (scoped) with the configured database provider.
    /// A module owns at most one DbContext; the platform creates its tables at startup.
    /// </summary>
    public abstract void AddDbContext<TContext>()
        where TContext : ModuleDbContext;

    /// <summary>
    /// Registers <typeparamref name="THandler"/> (scoped) for <typeparamref name="TEvent"/>.
    /// The module needs a DbContext, which holds the inbox.
    /// </summary>
    public abstract void AddEventHandler<TEvent, THandler>()
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>;
}
