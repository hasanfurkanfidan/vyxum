using Microsoft.EntityFrameworkCore;
using Shouldly;
using Vyxum.Modules.Products;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Modules.Tests;

/// <summary>
/// The whole catalog on request (<c>products.catalog-requested</c>, sent by the paid Integration module, which keeps its
/// own copy of the products and cannot know the ones that existed before it was loaded).
/// </summary>
public class CatalogRequestedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task SeedAsync(ProductsTestApp app, int count)
    {
        await app.WithDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                db.Products.Add(new Product { Id = Guid.NewGuid(), Code = "P" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), Name = "Urun " + i, IsActive = i % 7 != 0, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            }

            await db.SaveChangesAsync(Ct);
            return 0;
        });
    }

    private static async Task<List<ProductSaved>> SavedAsync(ProductsTestApp app) =>
        [.. (await app.OutboxAsync()).Where(m => m.EventName == "products.product-saved").Select(m => (ProductSaved)IntegrationEventSerializer.Deserialize(m.Payload, typeof(ProductSaved)))];

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Every_product_is_published_once_in_pages_and_nothing_changes(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        // More than two pages, so the paging is exercised.
        var count = (ProductService.CatalogPageSize * 2) + 37;
        await SeedAsync(app, count);
        var before = await app.WithDbAsync(db => db.Products.AsNoTracking().OrderBy(p => p.Code).Select(p => p.UpdatedAtUtc).ToListAsync(Ct));

        await app.SendEventAsync(new CatalogRequested(Guid.NewGuid()));

        var saved = await SavedAsync(app);
        saved.Count.ShouldBe(count);
        saved.Select(s => s.Code).Distinct().Count().ShouldBe(count);
        saved.Count(s => !s.IsActive).ShouldBe(Enumerable.Range(0, count).Count(i => i % 7 == 0));
        saved.ShouldAllBe(s => s.SaleUnit == "Kg");
        var after = await app.WithDbAsync(db => db.Products.AsNoTracking().OrderBy(p => p.Code).Select(p => p.UpdatedAtUtc).ToListAsync(Ct));
        after.ShouldBe(before);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_empty_catalog_publishes_nothing_and_the_request_is_done(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        await app.SendEventAsync(new CatalogRequested(Guid.NewGuid()));

        (await SavedAsync(app)).ShouldBeEmpty();
        (await app.OutboxAsync()).Where(m => m.EventName == "products.catalog-requested").ShouldAllBe(m => m.ProcessedAt != null && m.LastError == null);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Redelivery_of_one_request_does_not_publish_the_catalog_twice(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        await SeedAsync(app, 5);
        await app.SendEventAsync(new CatalogRequested(Guid.NewGuid()));

        await app.WithDbAsync(async db =>
        {
            await db.OutboxMessages.Where(m => m.EventName == "products.catalog-requested").ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, (DateTime?)null), Ct);
            return 0;
        });
        await app.DispatchAsync();

        (await SavedAsync(app)).Count.ShouldBe(5);
    }

    [Fact]
    public void The_request_has_the_name_and_version_the_sender_uses_and_this_module_only_handles_it()
    {
        var attribute = IntegrationEventAttribute.Find(typeof(CatalogRequested));
        attribute.ShouldNotBeNull();
        attribute.Name.ShouldBe("products.catalog-requested");
        attribute.Version.ShouldBe(1);
        new ProductsModule().Manifest.Publishes.ShouldNotContain(typeof(CatalogRequested));
    }
}
