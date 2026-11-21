using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Notifications;

/// <summary>Records a notification for a placed order.</summary>
public sealed class OrderPlacedHandler(NotificationsDbContext db, HandlerFaults faults) : IIntegrationEventHandler<OrderPlaced>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderPlaced integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            OrderId = integrationEvent.OrderId,
            Message = $"Order placed: {integrationEvent.Quantity} x {integrationEvent.Product}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        // Fails after saving, to show that the handler's writes are rolled back with it.
        faults.ThrowIfPending();
    }
}

/// <summary>Lets tests make <see cref="OrderPlacedHandler"/> fail a given number of times.</summary>
public sealed class HandlerFaults
{
    private int _remaining;

    /// <summary>Number of upcoming handler calls that fail.</summary>
    public int Remaining
    {
        get => Volatile.Read(ref _remaining);
        set => Volatile.Write(ref _remaining, value);
    }

    internal void ThrowIfPending()
    {
        if (Interlocked.Decrement(ref _remaining) >= 0)
        {
            throw new InvalidOperationException("Simulated handler failure.");
        }

        Interlocked.Exchange(ref _remaining, 0);
    }
}
