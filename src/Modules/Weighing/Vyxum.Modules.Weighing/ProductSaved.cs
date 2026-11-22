using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Weighing;

/// <summary>
/// This module's copy of the Products module's event contract. Modules never reference each
/// other; the event name and version connect them. Carries the full current state of a product.
/// </summary>
/// <param name="ProductId">The product id.</param>
/// <param name="Code">PLU code.</param>
/// <param name="Name">Product name.</param>
/// <param name="UnitPrice">Price per kg, if set.</param>
/// <param name="PackageTare">Package tare in kg, if set.</param>
/// <param name="ShelfLifeDays">Shelf life in days, if set.</param>
/// <param name="BarcodePrefix">Barcode prefix, if set.</param>
/// <param name="IsActive">Whether the product is active.</param>
/// <param name="TargetWeight">Target net weight in kg, if set.</param>
/// <param name="LowerTolerance">Allowed amount below the target in kg, if set.</param>
/// <param name="UpperTolerance">Allowed amount above the target in kg, if set.</param>
/// <param name="Ingredients">Ingredients, if set (not used by Weighing).</param>
/// <param name="SaleUnit">Kg or Piece (not used by Weighing).</param>
/// <param name="UnitCost">Cost per kg, if set (not used by Weighing).</param>
[IntegrationEvent("products.product-saved", 1)]
public sealed record ProductSaved(
    Guid ProductId,
    string Code,
    string Name,
    decimal? UnitPrice,
    decimal? PackageTare,
    int? ShelfLifeDays,
    string? BarcodePrefix,
    bool IsActive,
    decimal? TargetWeight = null,
    decimal? LowerTolerance = null,
    decimal? UpperTolerance = null,
    string? Ingredients = null,
    string? SaleUnit = null,
    decimal? UnitCost = null) : IIntegrationEvent;
