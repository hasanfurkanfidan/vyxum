using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>An integration event handler a module registered.</summary>
public sealed class EventHandlerRegistration
{
    internal EventHandlerRegistration(
        string moduleId,
        Type eventType,
        Type handlerType,
        Func<IServiceProvider, IIntegrationEvent, IntegrationEventContext, CancellationToken, Task> invoke)
    {
        ModuleId = moduleId;
        EventType = eventType;
        HandlerType = handlerType;
        Invoke = invoke;
    }

    /// <summary>Id of the module that owns the handler.</summary>
    public string ModuleId { get; }

    /// <summary>The handled event type.</summary>
    public Type EventType { get; }

    /// <summary>The handler type.</summary>
    public Type HandlerType { get; }

    /// <summary>Name stored in the inbox: the handler's full type name.</summary>
    public string HandlerName => HandlerType.FullName ?? HandlerType.Name;

    /// <summary>Resolves the handler from a scope and calls it.</summary>
    internal Func<IServiceProvider, IIntegrationEvent, IntegrationEventContext, CancellationToken, Task> Invoke { get; }
}
