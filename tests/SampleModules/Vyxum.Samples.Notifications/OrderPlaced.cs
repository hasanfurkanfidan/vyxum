using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Notifications;

/// <summary>
/// This module's copy of the Orders module's event contract. Modules never reference each
/// other; the event name and version connect them.
/// </summary>
/// <param name="OrderId">The order id.</param>
/// <param name="Product">Ordered product.</param>
/// <param name="Quantity">Ordered quantity.</param>
[IntegrationEvent("samples.order-placed", 1)]
public sealed record OrderPlaced(Guid OrderId, string Product, int Quantity) : IIntegrationEvent;
