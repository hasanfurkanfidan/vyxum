using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Paid;

/// <summary>This module's copy of the Orders module's event contract.</summary>
/// <param name="OrderId">The order id.</param>
/// <param name="Product">Ordered product.</param>
/// <param name="Quantity">Ordered quantity.</param>
[IntegrationEvent("samples.order-placed", 1)]
public sealed record OrderPlaced(Guid OrderId, string Product, int Quantity) : IIntegrationEvent;

/// <summary>Adds an entry for a placed order.</summary>
public sealed class OrderPlacedHandler(PaidDbContext db) : IIntegrationEventHandler<OrderPlaced>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderPlaced integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        db.Entries.Add(new Entry
        {
            Id = Guid.NewGuid(),
            Text = $"Order: {integrationEvent.Quantity} x {integrationEvent.Product}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
