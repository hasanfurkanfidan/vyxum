using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Orders;

/// <summary>Published when an order is placed.</summary>
/// <param name="OrderId">The order id.</param>
/// <param name="Product">Ordered product.</param>
/// <param name="Quantity">Ordered quantity.</param>
[IntegrationEvent("samples.order-placed", 1)]
public sealed record OrderPlaced(Guid OrderId, string Product, int Quantity) : IIntegrationEvent;
