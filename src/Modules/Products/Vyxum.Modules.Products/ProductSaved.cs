using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

/// <summary>Published whenever a product is created, changed, activated or deactivated. Carries the full current state.</summary>
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
/// <param name="Ingredients">Ingredients with allergens between asterisks (<c>*milk*</c>), if set.</param>
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
    string? Ingredients = null) : IIntegrationEvent;
