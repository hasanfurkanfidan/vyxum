namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Records that a handler processed a message, in the handler module's <c>{prefix}_InboxMessages</c>
/// table. Its key (<see cref="MessageId"/>, <see cref="HandlerName"/>) prevents a second run.
/// </summary>
public sealed class InboxMessage
{
    /// <summary>The outbox message id.</summary>
    public Guid MessageId { get; init; }

    /// <summary>Full type name of the handler.</summary>
    public required string HandlerName { get; init; }

    /// <summary>UTC time the handler committed.</summary>
    public DateTime ProcessedAt { get; init; }
}
