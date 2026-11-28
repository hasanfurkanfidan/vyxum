using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

/// <summary>
/// A material card an ERP connector (the paid Integration module) chose to bring in. This module's own copy of the
/// event: modules do not reference each other, so only the name and version tie it to the sender. The sender has
/// already decided what to do when the product exists; this module only applies it, and never deletes.
/// </summary>
/// <param name="ConnectionId">The ERP connection the card was read from.</param>
/// <param name="ConnectionName">The connection's name, for logs.</param>
/// <param name="ErpItemCode">The item code in the ERP.</param>
/// <param name="Code">The product code to create or update.</param>
/// <param name="Name">The name.</param>
/// <param name="Unit">The ERP inventory unit, such as <c>kg</c> or <c>adet</c>; an unknown one is ignored.</param>
/// <param name="Barcode">The ERP barcode. Carried for the future; a product card has no field for it yet.</param>
/// <param name="LotTracking">Whether the ERP manages the item by lot. Carried for the future; a product card has no field for it yet.</param>
/// <param name="Active">False when the card is inactive in the ERP.</param>
/// <param name="IfExists"><c>Update</c>: change the existing product; anything else (<c>Leave</c>): keep it as it is.</param>
[IntegrationEvent("integration.product-received", 1)]
public sealed record ProductReceived(
    Guid ConnectionId,
    string? ConnectionName,
    string ErpItemCode,
    string Code,
    string Name,
    string? Unit,
    string? Barcode,
    bool LotTracking,
    bool Active,
    string IfExists) : IIntegrationEvent;
