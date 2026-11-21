namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Handles an integration event, possibly published by another module. Register it with
/// <see cref="ModuleContext.AddEventHandler{TEvent, THandler}"/>. The handler runs in a transaction
/// of its module's DbContext (resolve that context from DI); its writes and the inbox record that
/// prevents a second run are committed together. Throwing rolls everything back and the event is retried.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>Handles one event.</summary>
    Task HandleAsync(TEvent integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken);
}
