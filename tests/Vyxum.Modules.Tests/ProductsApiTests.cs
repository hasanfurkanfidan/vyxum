using System.Net;
using System.Text.Json;
using Shouldly;
using Vyxum.Modules.Products;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Modules.Tests;

public class ProductsApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Body(string code = "101", string name = "Cheese", string? unitPrice = "780.00", string? packageTare = "0.012", int? shelfLifeDays = 30, string? targetWeight = null, string? lowerTolerance = null, string? upperTolerance = null) => new
    {
        code,
        name,
        unitPrice,
        packageTare,
        shelfLifeDays,
        targetWeight,
        lowerTolerance,
        upperTolerance,
        barcodePrefix = "28",
        ingredients = "Milk*, salt",
    };

    private static async Task<Guid> CreateAsync(ProductsTestApp app, object body)
    {
        var (response, json) = await app.SendAsync(HttpMethod.Post, "/api/products", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return json.GetProperty("id").GetGuid();
    }

    private static string[] Codes(JsonElement list) => [.. list.EnumerateArray().Select(p => p.GetProperty("code").GetString()!)];

    private static string[] ErrorKeys(JsonElement problem, string field) =>
        [.. problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!)];

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Create_get_and_update_round_trip(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        var (created, json) = await app.SendAsync(HttpMethod.Post, "/api/products", Body());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        json.GetProperty("code").GetString().ShouldBe("101");
        json.GetProperty("name").GetString().ShouldBe("Cheese");
        json.GetProperty("unitPrice").GetString().ShouldBe("780.00");
        json.GetProperty("packageTare").GetString().ShouldBe("0.012");
        json.GetProperty("shelfLifeDays").GetInt32().ShouldBe(30);
        json.GetProperty("barcodePrefix").GetString().ShouldBe("28");
        json.GetProperty("ingredients").GetString().ShouldBe("Milk*, salt");
        json.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        json.GetProperty("updatedAt").GetDateTime().Kind.ShouldBe(DateTimeKind.Utc);
        json.GetProperty("targetWeight").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("lowerTolerance").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("upperTolerance").ValueKind.ShouldBe(JsonValueKind.Null);
        var id = json.GetProperty("id").GetGuid();

        var (got, gotJson) = await app.SendAsync(HttpMethod.Get, $"/api/products/{id}");
        got.StatusCode.ShouldBe(HttpStatusCode.OK);
        gotJson.GetProperty("unitPrice").GetString().ShouldBe("780.00");

        var (updated, updatedJson) = await app.SendAsync(
            HttpMethod.Put, $"/api/products/{id}", Body("101", "Aged cheese", "825.5", null, null));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        updatedJson.GetProperty("name").GetString().ShouldBe("Aged cheese");
        updatedJson.GetProperty("unitPrice").GetString().ShouldBe("825.50");
        updatedJson.GetProperty("packageTare").ValueKind.ShouldBe(JsonValueKind.Null);
        updatedJson.GetProperty("shelfLifeDays").ValueKind.ShouldBe(JsonValueKind.Null);
        updatedJson.GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Target_weight_and_tolerances_round_trip_and_can_be_cleared(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        var (created, json) = await app.SendAsync(
            HttpMethod.Post, "/api/products", Body(targetWeight: "0.25", lowerTolerance: "0.005", upperTolerance: "0.0100"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        json.GetProperty("targetWeight").GetString().ShouldBe("0.25");
        json.GetProperty("lowerTolerance").GetString().ShouldBe("0.005");
        json.GetProperty("upperTolerance").GetString().ShouldBe("0.01");
        var id = json.GetProperty("id").GetGuid();

        var (_, got) = await app.SendAsync(HttpMethod.Get, $"/api/products/{id}");
        got.GetProperty("targetWeight").GetString().ShouldBe("0.25");

        // Only a target, zero tolerances and an empty tolerance are fine.
        var (partial, partialJson) = await app.SendAsync(
            HttpMethod.Put, $"/api/products/{id}", Body(targetWeight: "0.250", lowerTolerance: "0", upperTolerance: ""));
        partial.StatusCode.ShouldBe(HttpStatusCode.OK);
        partialJson.GetProperty("targetWeight").GetString().ShouldBe("0.25");
        partialJson.GetProperty("lowerTolerance").GetString().ShouldBe("0.00");
        partialJson.GetProperty("upperTolerance").ValueKind.ShouldBe(JsonValueKind.Null);

        var (cleared, clearedJson) = await app.SendAsync(HttpMethod.Put, $"/api/products/{id}", Body());
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        clearedJson.GetProperty("targetWeight").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Sale_unit_and_unit_cost_round_trip_and_a_piece_needs_its_weight(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        // Older clients send neither: the product is sold by weight and has no cost.
        var (_, plain) = await app.SendAsync(HttpMethod.Post, "/api/products", Body("101"));
        plain.GetProperty("saleUnit").GetString().ShouldBe("Kg");
        plain.GetProperty("unitCost").ValueKind.ShouldBe(JsonValueKind.Null);

        var (needsWeight, needsWeightJson) = await app.SendAsync(HttpMethod.Post, "/api/products", new { code = "102", name = "Cookies", saleUnit = "Piece" });
        needsWeight.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(needsWeightJson, "targetWeight").ShouldBe(["products.PieceNeedsWeight"]);

        var (badCost, badCostJson) = await app.SendAsync(HttpMethod.Post, "/api/products", new { code = "102", name = "Cookies", unitCost = "-1" });
        badCost.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(badCostJson, "unitCost").ShouldBe(["products.NegativeNotAllowed"]);

        var (created, json) = await app.SendAsync(HttpMethod.Post, "/api/products", new { code = "102", name = "Cookies", saleUnit = "Piece", targetWeight = "0.020", unitCost = "412.5" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        json.GetProperty("saleUnit").GetString().ShouldBe("Piece");
        json.GetProperty("unitCost").GetString().ShouldBe("412.50");
        var id = json.GetProperty("id").GetGuid();
        var (_, got) = await app.SendAsync(HttpMethod.Get, $"/api/products/{id}");
        got.GetProperty("saleUnit").GetString().ShouldBe("Piece");
        got.GetProperty("targetWeight").GetString().ShouldBe("0.02");

        var saved = (await app.OutboxAsync()).Select(m => JsonSerializer.Deserialize<ProductSaved>(m.Payload, JsonSerializerOptions.Web)!).Last();
        saved.SaleUnit.ShouldBe("Piece");
        saved.UnitCost.ShouldBe(412.5m);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Target_weight_and_tolerances_are_validated(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        var (needsTarget, needsTargetJson) = await app.SendAsync(
            HttpMethod.Post, "/api/products", Body(lowerTolerance: "0.005", upperTolerance: "0.01"));
        needsTarget.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(needsTargetJson, "lowerTolerance").ShouldBe(["products.ToleranceNeedsTarget"]);
        ErrorKeys(needsTargetJson, "upperTolerance").ShouldBe(["products.ToleranceNeedsTarget"]);

        var (bad, badJson) = await app.SendAsync(
            HttpMethod.Post, "/api/products", Body(targetWeight: "0,25", lowerTolerance: "-0.1", upperTolerance: "1e3"));
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(badJson, "targetWeight").ShouldBe(["products.InvalidNumber"]);
        ErrorKeys(badJson, "lowerTolerance").ShouldBe(["products.NegativeNotAllowed"]);
        ErrorKeys(badJson, "upperTolerance").ShouldBe(["products.InvalidNumber"]);

        var (negative, negativeJson) = await app.SendAsync(HttpMethod.Post, "/api/products", Body(targetWeight: "-1"));
        negative.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(negativeJson, "targetWeight").ShouldBe(["products.NegativeNotAllowed"]);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Unknown_id_is_404(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        var id = Guid.NewGuid();

        (await app.SendAsync(HttpMethod.Get, $"/api/products/{id}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.SendAsync(HttpMethod.Put, $"/api/products/{id}", Body())).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.SendAsync(HttpMethod.Post, $"/api/products/{id}/deactivate")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.SendAsync(HttpMethod.Post, $"/api/products/{id}/activate")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.OutboxAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Code_must_be_unique_on_create_and_update(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        await CreateAsync(app, Body("101", "Cheese"));
        var otherId = await CreateAsync(app, Body("102", "Olives"));

        var (create, createJson) = await app.SendAsync(HttpMethod.Post, "/api/products", Body("101", "Other"));
        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(createJson, "code").ShouldBe(["products.CodeTaken"]);

        var (update, updateJson) = await app.SendAsync(HttpMethod.Put, $"/api/products/{otherId}", Body("101", "Olives"));
        update.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(updateJson, "code").ShouldBe(["products.CodeTaken"]);

        // Keeping its own code is fine, and the check ignores case on every provider.
        (await app.SendAsync(HttpMethod.Put, $"/api/products/{otherId}", Body("102", "Black olives"))).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (letters, _) = await app.SendAsync(HttpMethod.Post, "/api/products", Body("ab1", "Bread"));
        letters.StatusCode.ShouldBe(HttpStatusCode.Created);
        var (upper, upperJson) = await app.SendAsync(HttpMethod.Post, "/api/products", Body("AB1", "Bread 2"));
        upper.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(upperJson, "code").ShouldBe(["products.CodeTaken"]);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Validation_errors_use_field_names_and_resx_keys(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        var (response, json) = await app.SendAsync(HttpMethod.Post, "/api/products", new
        {
            code = " ",
            name = new string('x', 201),
            unitPrice = "12,5",
            packageTare = "-0.1",
            shelfLifeDays = -1,
            barcodePrefix = new string('1', 17),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(json, "code").ShouldBe(["products.CodeRequired"]);
        ErrorKeys(json, "name").ShouldBe(["products.NameTooLong"]);
        ErrorKeys(json, "unitPrice").ShouldBe(["products.InvalidNumber"]);
        ErrorKeys(json, "packageTare").ShouldBe(["products.NegativeNotAllowed"]);
        ErrorKeys(json, "shelfLifeDays").ShouldBe(["products.NegativeNotAllowed"]);
        ErrorKeys(json, "barcodePrefix").ShouldBe(["products.BarcodePrefixTooLong"]);

        var (missing, missingJson) = await app.SendAsync(HttpMethod.Post, "/api/products", new { code = new string('1', 33), name = "" });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(missingJson, "code").ShouldBe(["products.CodeTooLong"]);
        ErrorKeys(missingJson, "name").ShouldBe(["products.NameRequired"]);

        (await app.OutboxAsync()).ShouldBeEmpty();
        (await app.WithDbAsync(db => Task.FromResult(db.Products.Count()))).ShouldBe(0);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.Sqlite, "1e3")]
    [InlineData(DatabaseProvider.Sqlite, "1,000.50")]
    [InlineData(DatabaseProvider.Sqlite, "abc")]
    [InlineData(DatabaseProvider.Sqlite, "1.23456")]
    public async Task Numbers_are_parsed_strictly_with_invariant_culture(DatabaseProvider provider, string? invalid = null)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        if (invalid is not null)
        {
            var (bad, badJson) = await app.SendAsync(HttpMethod.Post, "/api/products", Body(unitPrice: invalid));
            bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            ErrorKeys(badJson, "unitPrice").ShouldBe(["products.InvalidNumber"]);
            return;
        }

        var (ok, json) = await app.SendAsync(HttpMethod.Post, "/api/products", Body(unitPrice: "1234.5", packageTare: "0.0125"));
        ok.StatusCode.ShouldBe(HttpStatusCode.Created);
        json.GetProperty("unitPrice").GetString().ShouldBe("1234.50");
        json.GetProperty("packageTare").GetString().ShouldBe("0.0125");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Search_matches_code_or_name_and_list_is_ordered_by_code(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        await CreateAsync(app, Body("200", "Olives"));
        await CreateAsync(app, Body("101", "White cheese"));
        await CreateAsync(app, Body("150", "Cheddar"));

        var (_, all) = await app.SendAsync(HttpMethod.Get, "/api/products");
        Codes(all).ShouldBe(["101", "150", "200"]);

        var (_, byName) = await app.SendAsync(HttpMethod.Get, "/api/products?search=CHEE");
        Codes(byName).ShouldBe(["101"]);

        var (_, byCode) = await app.SendAsync(HttpMethod.Get, "/api/products?search=15");
        Codes(byCode).ShouldBe(["150"]);

        var (_, contains) = await app.SendAsync(HttpMethod.Get, "/api/products?search=ed");
        Codes(contains).ShouldBe(["150"]);

        var (_, none) = await app.SendAsync(HttpMethod.Get, "/api/products?search=zzz");
        Codes(none).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Inactive_products_are_hidden_unless_requested_and_can_be_reactivated(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);
        var id = await CreateAsync(app, Body("101", "Cheese"));
        await CreateAsync(app, Body("102", "Olives"));

        var (deactivated, deactivatedJson) = await app.SendAsync(HttpMethod.Post, $"/api/products/{id}/deactivate");
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        deactivatedJson.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        Codes((await app.SendAsync(HttpMethod.Get, "/api/products")).Body).ShouldBe(["102"]);
        Codes((await app.SendAsync(HttpMethod.Get, "/api/products?includeInactive=true")).Body).ShouldBe(["101", "102"]);
        Codes((await app.SendAsync(HttpMethod.Get, "/api/products?includeInactive=false")).Body).ShouldBe(["102"]);
        (await app.SendAsync(HttpMethod.Get, $"/api/products/{id}")).Body.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        var (activated, activatedJson) = await app.SendAsync(HttpMethod.Post, $"/api/products/{id}/activate");
        activated.StatusCode.ShouldBe(HttpStatusCode.OK);
        activatedJson.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        Codes((await app.SendAsync(HttpMethod.Get, "/api/products")).Body).ShouldBe(["101", "102"]);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Every_change_writes_a_product_saved_event_to_the_outbox(DatabaseProvider provider)
    {
        await using var app = await ProductsTestApp.StartAsync(provider);

        var id = await CreateAsync(app, Body("101", "Cheese"));
        await app.SendAsync(HttpMethod.Put, $"/api/products/{id}", Body("101", "Aged cheese", "900", "0.02", 60, "0.25", "0.005", "0.01"));
        await app.SendAsync(HttpMethod.Post, $"/api/products/{id}/deactivate");
        await app.SendAsync(HttpMethod.Post, $"/api/products/{id}/activate");
        // Rejected requests publish nothing.
        await app.SendAsync(HttpMethod.Post, "/api/products", Body("101", "Duplicate"));

        var events = (await app.OutboxAsync())
            .Select(m =>
            {
                m.EventName.ShouldBe("products.product-saved");
                m.EventVersion.ShouldBe(1);
                return JsonSerializer.Deserialize<ProductSaved>(m.Payload, JsonSerializerOptions.Web)!;
            })
            .ToList();

        events.Count.ShouldBe(4);
        events.ShouldAllBe(e => e.ProductId == id && e.Code == "101");
        events.Select(e => e.Name).ShouldBe(["Cheese", "Aged cheese", "Aged cheese", "Aged cheese"]);
        events.Select(e => e.IsActive).ShouldBe([true, true, false, true]);
        events[0].UnitPrice.ShouldBe(780.00m);
        events[0].PackageTare.ShouldBe(0.012m);
        events[0].ShelfLifeDays.ShouldBe(30);
        events[0].BarcodePrefix.ShouldBe("28");
        events[0].Ingredients.ShouldBe("Milk*, salt");
        events[0].TargetWeight.ShouldBeNull();
        events[1].TargetWeight.ShouldBe(0.25m);
        events[1].LowerTolerance.ShouldBe(0.005m);
        events[1].UpperTolerance.ShouldBe(0.01m);
        events[3].UpperTolerance.ShouldBe(0.01m);
        events[1].UnitPrice.ShouldBe(900m);
        events[1].ShelfLifeDays.ShouldBe(60);

        // An event stored before the target fields existed still reads, with them empty.
        JsonSerializer.Deserialize<ProductSaved>(
            """{"productId":"0e7d3f7a-5d0e-4a39-9f0e-000000000417","code":"1","name":"N","unitPrice":null,"packageTare":null,"shelfLifeDays":null,"barcodePrefix":null,"isActive":true}""",
            JsonSerializerOptions.Web)!.TargetWeight.ShouldBeNull();
        JsonSerializer.Deserialize<ProductSaved>(
            """{"productId":"0e7d3f7a-5d0e-4a39-9f0e-000000000417","code":"1","name":"N","isActive":true}""",
            JsonSerializerOptions.Web)!.Ingredients.ShouldBeNull();
    }

    [Fact]
    public void Module_manifest_and_strings_are_complete()
    {
        var manifest = new ProductsModule().Manifest;
        manifest.Id.ShouldBe("products");
        manifest.Provides.ShouldContain((Capability)"products.catalog");
        manifest.Publishes.ShouldBe([typeof(ProductSaved)]);

        var english = new System.Globalization.CultureInfo("en");
        var turkish = new System.Globalization.CultureInfo("tr");
        foreach (var key in new[]
        {
            "CodeRequired", "CodeTaken", "CodeTooLong", "NameRequired", "NameTooLong", "InvalidNumber", "NegativeNotAllowed",
            "Title", "Breadcrumb", "CountSuffix", "AddProduct", "Code", "Product", "UnitPricePerKg", "PackageTare", "ShelfLife", "Days",
            "BarcodePrefix", "Ingredients", "IngredientsHint", "Name", "Save", "Deactivate", "Activate", "ShowInactive", "Inactive",
            "Saved", "NewProduct", "Empty", "SearchPlaceholder", "TargetWeight", "LowerTolerance", "UpperTolerance", "ToleranceHint", "ToleranceNeedsTarget",
        })
        {
            ProductsStrings.Get(key, english).ShouldNotBeNullOrWhiteSpace(key);
            ProductsStrings.Get(key, turkish).ShouldNotBeNullOrWhiteSpace(key);
        }

        ProductsStrings.Get("TargetWeight", turkish).ShouldBe("Hedef ağırlık (kg)");
        ProductsStrings.Get("UpperTolerance", english).ShouldBe("Upper tolerance (kg)");
        ProductsStrings.Get("Title", english).ShouldBe("Product cards");
        ProductsStrings.Get("Title", turkish).ShouldBe("Ürün kartları");
        ProductsStrings.Get("SearchPlaceholder", turkish).ShouldBe("PLU ya da ad");
    }
}
