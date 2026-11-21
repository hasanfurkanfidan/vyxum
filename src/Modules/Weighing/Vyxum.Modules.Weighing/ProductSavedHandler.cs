using Microsoft.EntityFrameworkCore;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Weighing;

/// <summary>
/// Keeps <c>wgh_Products</c> up to date: inserts or replaces the product by id. The inbox prevents a
/// second run of the same message; an event older than the stored copy (out-of-order delivery) is ignored.
/// </summary>
public sealed class ProductSavedHandler(WeighingDbContext db) : IIntegrationEventHandler<ProductSaved>
{
    /// <inheritdoc />
    public async Task HandleAsync(ProductSaved integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(context);

        var product = await db.Products.SingleOrDefaultAsync(p => p.ProductId == integrationEvent.ProductId, cancellationToken);
        if (product is null)
        {
            product = new WeighingProduct { ProductId = integrationEvent.ProductId, Code = integrationEvent.Code, Name = integrationEvent.Name };
            db.Products.Add(product);
        }
        else if (product.SourceOccurredAtUtc > context.OccurredAt)
        {
            return;
        }

        product.Code = integrationEvent.Code;
        product.Name = integrationEvent.Name;
        product.UnitPrice = integrationEvent.UnitPrice;
        product.PackageTare = integrationEvent.PackageTare;
        product.ShelfLifeDays = integrationEvent.ShelfLifeDays;
        product.BarcodePrefix = integrationEvent.BarcodePrefix;
        product.IsActive = integrationEvent.IsActive;
        product.TargetWeight = integrationEvent.TargetWeight;
        product.LowerTolerance = integrationEvent.LowerTolerance;
        product.UpperTolerance = integrationEvent.UpperTolerance;
        product.SourceOccurredAtUtc = context.OccurredAt;
        await db.SaveChangesAsync(cancellationToken);
    }
}
