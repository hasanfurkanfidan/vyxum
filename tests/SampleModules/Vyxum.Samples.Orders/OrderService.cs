namespace Vyxum.Samples.Orders;

/// <summary>Places orders.</summary>
public sealed class OrderService(OrdersDbContext db)
{
    /// <summary>Stores a new order and publishes <see cref="OrderPlaced"/> in the same transaction; returns the order id.</summary>
    public async Task<Guid> PlaceOrderAsync(string product, int quantity, CancellationToken cancellationToken = default)
    {
        var order = new Order { Id = Guid.NewGuid(), Product = product, Quantity = quantity, PlacedAt = DateTime.UtcNow };
        db.Orders.Add(order);
        db.Publish(new OrderPlaced(order.Id, order.Product, order.Quantity));
        await db.SaveChangesAsync(cancellationToken);
        return order.Id;
    }
}
