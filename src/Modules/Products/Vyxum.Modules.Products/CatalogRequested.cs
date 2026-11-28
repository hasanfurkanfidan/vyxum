using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

/// <summary>
/// Another module (the paid Integration module) asks for the whole product catalog, because it keeps its own copy and
/// cannot know the products that existed before it was loaded. This module's own copy of the event: only the name and
/// version tie it to the sender. It answers by publishing <see cref="ProductSaved"/> for every product.
/// </summary>
/// <param name="RequestId">Identifies the request, for the sender's records.</param>
[IntegrationEvent("products.catalog-requested", 1)]
public sealed record CatalogRequested(Guid RequestId) : IIntegrationEvent;
