namespace Vyxum.Platform.Abstractions;

/// <summary>Delivery information for an integration event.</summary>
/// <param name="MessageId">Unique id of the published message; the same on every retry.</param>
/// <param name="OccurredAt">UTC time the event was published.</param>
/// <param name="EventName">Event name from <see cref="IntegrationEventAttribute"/>.</param>
/// <param name="EventVersion">Event version from <see cref="IntegrationEventAttribute"/>.</param>
public sealed record IntegrationEventContext(Guid MessageId, DateTime OccurredAt, string EventName, int EventVersion);
