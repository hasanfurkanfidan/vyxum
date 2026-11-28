using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Vyxum.Modules.Products;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Modules.Tests;

/// <summary>
/// Material cards an ERP connector brings in (<c>integration.product-received</c>, sent by the paid Integration module):
/// the Products module only applies what the event says.
/// </summary>
public class ProductReceivedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProductReceived Card(
        string code = "KJ-2001",
        string name = "Kaju",
        string? unit = "kg",
        bool active = true,
        string ifExists = "Leave",
        string? barcode = "8690000020011",
        bool lot = true) =>
        new(Guid.NewGuid(), "Merkez ERP", code, code, name, unit, barcode, lot, active, ifExists);

    private static Task<List<Product>> ProductsAsync(ProductsTestApp app) =>
        app.WithDbAsync(db => db.Products.AsNoTracking().OrderBy(p => p.Code).ToListAsync(Ct));

    private static async Task<List<ProductSaved>> SavedAsync(ProductsTestApp app) =>
        [.. (await app.OutboxAsync()).Where(m => m.EventName == "products.product-saved").Select(m => (ProductSaved)IntegrationEventSerializer.Deserialize(m.Payload, typeof(ProductSaved)))];

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_missing_product_is_made_from_the_card_and_published(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        await app.SendEventAsync(Card(name: "Kaju 1. sınıf", unit: "KG", active: true));

        var product = (await ProductsAsync(app)).ShouldHaveSingleItem();
        product.Code.ShouldBe("KJ-2001");
        product.Name.ShouldBe("Kaju 1. sınıf");
        product.IsActive.ShouldBeTrue();
        product.SaleUnit.ShouldBe(SaleUnit.Kg);
        product.UnitPrice.ShouldBeNull();
        var saved = (await SavedAsync(app)).ShouldHaveSingleItem();
        saved.Code.ShouldBe("KJ-2001");
        saved.ProductId.ShouldBe(product.Id);
        (await app.OutboxAsync()).Where(m => m.EventName == "integration.product-received").ShouldAllBe(m => m.ProcessedAt != null);

        // It is an ordinary product card: the API lists it.
        var (status, body) = await app.SendAsync(HttpMethod.Get, "/api/products");
        status.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.EnumerateArray().Select(p => p.GetProperty("code").GetString()).ShouldBe(["KJ-2001"]);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_inactive_card_makes_an_inactive_product(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        await app.SendEventAsync(Card(active: false));

        (await ProductsAsync(app)).ShouldHaveSingleItem().IsActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_existing_product_changes_only_when_the_card_says_update_and_only_name_unit_and_active_state(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        var (created, _) = await app.SendAsync(HttpMethod.Post, "/api/products", new
        {
            code = "KJ-2001",
            name = "Kaju (eski)",
            unitPrice = "780.00",
            packageTare = "0.012",
            shelfLifeDays = 30,
            barcodePrefix = "28",
            ingredients = "Kaju*",
            targetWeight = "0.250",
            lowerTolerance = "0.005",
            upperTolerance = "0.010",
            unitCost = "412.50",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        await app.SendEventAsync(Card(name: "Kaju yeni", active: false, ifExists: "Update", unit: "adet"));

        var product = (await ProductsAsync(app)).ShouldHaveSingleItem();
        product.Name.ShouldBe("Kaju yeni");
        product.IsActive.ShouldBeFalse();
        product.SaleUnit.ShouldBe(SaleUnit.Piece);
        product.UnitPrice.ShouldBe(780.00m);
        product.PackageTare.ShouldBe(0.012m);
        product.ShelfLifeDays.ShouldBe(30);
        product.BarcodePrefix.ShouldBe("28");
        product.Ingredients.ShouldBe("Kaju*");
        product.TargetWeight.ShouldBe(0.250m);
        product.LowerTolerance.ShouldBe(0.005m);
        product.UpperTolerance.ShouldBe(0.010m);
        product.UnitCost.ShouldBe(412.50m);
        (await SavedAsync(app)).Count.ShouldBe(2);
        (await SavedAsync(app)).Last().Name.ShouldBe("Kaju yeni");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_card_that_says_leave_changes_nothing_and_publishes_nothing(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        (await app.SendAsync(HttpMethod.Post, "/api/products", new { code = "KJ-2001", name = "Kaju (eski)" })).Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var before = (await SavedAsync(app)).Count;

        await app.SendEventAsync(Card(name: "Başka ad", active: false, ifExists: "Leave"));
        await app.SendEventAsync(Card(name: "Başka ad", active: false, ifExists: "Ask"));

        var product = (await ProductsAsync(app)).ShouldHaveSingleItem();
        product.Name.ShouldBe("Kaju (eski)");
        product.IsActive.ShouldBeTrue();
        (await SavedAsync(app)).Count.ShouldBe(before);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task The_code_is_matched_ignoring_case_and_no_second_product_is_made(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        (await app.SendAsync(HttpMethod.Post, "/api/products", new { code = "kj-2001", name = "Kaju" })).Response.StatusCode.ShouldBe(HttpStatusCode.Created);

        await app.SendEventAsync(Card(code: "KJ-2001", name: "Kaju yeni", ifExists: "Update"));

        var product = (await ProductsAsync(app)).ShouldHaveSingleItem();
        product.Code.ShouldBe("kj-2001");
        product.Name.ShouldBe("Kaju yeni");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_unknown_unit_or_a_piece_unit_without_a_piece_weight_leaves_the_sale_unit_alone(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        await app.SendEventAsync(Card(unit: "tur-xx-9"));
        await app.SendEventAsync(Card(unit: "adet", ifExists: "Update"));
        await app.SendEventAsync(Card(unit: null, ifExists: "Update", name: "Kaju 2"));

        var product = (await ProductsAsync(app)).ShouldHaveSingleItem();
        product.SaleUnit.ShouldBe(SaleUnit.Kg);
        product.Name.ShouldBe("Kaju 2");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_name_that_is_missing_or_too_long_is_made_usable(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        await app.SendEventAsync(Card(code: "A-1", name: "  "));
        await app.SendEventAsync(Card(code: "A-2", name: new string('x', 500)));

        var products = await ProductsAsync(app);
        products.Single(p => p.Code == "A-1").Name.ShouldBe("A-1");
        products.Single(p => p.Code == "A-2").Name.Length.ShouldBe(ProductLimits.NameMaxLength);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_card_without_a_usable_code_is_skipped_and_is_not_retried(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        await app.SendEventAsync(Card(code: "  "));
        await app.SendEventAsync(Card(code: new string('9', ProductLimits.CodeMaxLength + 1)));

        (await ProductsAsync(app)).ShouldBeEmpty();
        var received = (await app.OutboxAsync()).Where(m => m.EventName == "integration.product-received").ToList();
        received.Count.ShouldBe(2);
        received.ShouldAllBe(m => m.ProcessedAt != null && m.LastError == null);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task The_same_data_sent_again_changes_nothing_and_publishes_nothing(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        var card = Card(name: "Kaju", ifExists: "Update");

        await app.SendEventAsync(card);
        // Three messages with equal data (a read of the ERP that finds nothing new): one product, one ProductSaved.
        await app.SendEventAsync(card);
        await app.SendEventAsync(card with { Name = "Kaju" });

        (await ProductsAsync(app)).ShouldHaveSingleItem();
        (await SavedAsync(app)).ShouldHaveSingleItem();
        var inbox = await app.WithDbAsync(db => db.InboxMessages.CountAsync(Ct));
        inbox.ShouldBe(3);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Redelivery_of_one_message_after_a_crash_does_not_make_a_second_product(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        await app.SendEventAsync(Card());

        // Put the message back as not processed, as if the dispatcher had stopped before marking it.
        await app.WithDbAsync(async db =>
        {
            await db.OutboxMessages.Where(m => m.EventName == "integration.product-received").ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, (DateTime?)null), Ct);
            return 0;
        });
        await app.DispatchAsync();

        (await ProductsAsync(app)).ShouldHaveSingleItem();
        (await SavedAsync(app)).ShouldHaveSingleItem();
        (await app.OutboxAsync()).Where(m => m.EventName == "integration.product-received").ShouldAllBe(m => m.ProcessedAt != null);
    }

    [Fact]
    public void The_event_has_the_name_and_version_the_sender_uses()
    {
        var attribute = IntegrationEventAttribute.Find(typeof(ProductReceived));
        attribute.ShouldNotBeNull();
        attribute.Name.ShouldBe("integration.product-received");
        attribute.Version.ShouldBe(1);
        // This module only handles the event; the Integration module is its only publisher.
        new ProductsModule().Manifest.Publishes.ShouldNotContain(typeof(ProductReceived));
    }
}
