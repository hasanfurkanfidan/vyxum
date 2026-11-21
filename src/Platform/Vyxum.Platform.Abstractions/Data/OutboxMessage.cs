namespace Vyxum.Platform.Abstractions;

/// <summary>
/// An integration event waiting to be delivered, stored in the publishing module's
/// <c>{prefix}_OutboxMessages</c> table in the same transaction as the module's business data.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Message id; handlers see it as <see cref="IntegrationEventContext.MessageId"/>.</summary>
    public Guid Id { get; init; }

    /// <summary>Event name.</summary>
    public required string EventName { get; init; }

    /// <summary>Event version.</summary>
    public int EventVersion { get; init; }

    /// <summary>JSON payload.</summary>
    public required string Payload { get; init; }

    /// <summary>UTC publish time; messages are delivered roughly in this order.</summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>UTC time every handler succeeded, or null while pending.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Number of delivery attempts so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Errors of the last failed attempt.</summary>
    public string? LastError { get; set; }

    /// <summary>UTC time of the next retry after a failure; null means as soon as possible.</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>Maximum stored length of <see cref="LastError"/>.</summary>
    public const int LastErrorMaxLength = 2000;

    /// <summary>Creates a pending message for <paramref name="integrationEvent"/>.</summary>
    /// <exception cref="InvalidOperationException">The event type has no <see cref="IntegrationEventAttribute"/>.</exception>
    public static OutboxMessage Create(IIntegrationEvent integrationEvent, DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var type = integrationEvent.GetType();
        var attribute = IntegrationEventAttribute.Find(type)
            ?? throw new InvalidOperationException($"Integration event type '{type.FullName}' has no [IntegrationEvent] attribute.");

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventName = attribute.Name,
            EventVersion = attribute.Version,
            Payload = IntegrationEventSerializer.Serialize(integrationEvent),
            OccurredAt = occurredAtUtc,
        };
    }
}
