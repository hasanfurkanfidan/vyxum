using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

/// <summary>
/// Answers a catalog request with <see cref="ProductSaved"/> for every product. The platform's inbox keeps a request
/// from being answered twice; a second request simply publishes the current state again, which receivers accept.
/// </summary>
public sealed class CatalogRequestedHandler(ProductService products) : IIntegrationEventHandler<CatalogRequested>
{
    /// <inheritdoc />
    public async Task HandleAsync(CatalogRequested integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await products.PublishCatalogAsync(cancellationToken);
    }
}
