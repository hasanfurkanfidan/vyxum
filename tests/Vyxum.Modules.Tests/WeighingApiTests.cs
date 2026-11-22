using System.Net;
using System.Text.Json;
using Shouldly;
using Vyxum.Modules.Weighing;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Modules.Tests;

public class WeighingApiTests
{
    private const string Scale = "scale-1";

    private static readonly Guid Pistachio = Guid.Parse("0e7d3f7a-5d0e-4a39-9f0e-000000000417");
    private static readonly Guid Cashew = Guid.Parse("0e7d3f7a-5d0e-4a39-9f0e-000000000203");

    private static object Body(Guid? requestId = null, string? deviceId = Scale, Guid? productId = null, string? lot = null, string? tare = null) =>
        new { requestId = requestId ?? Guid.NewGuid(), deviceId, productId, lot, tare };

    private static ProductSaved Product(
        Guid id,
        string code = "0417",
        string name = "Pistachio",
        decimal? unitPrice = 780.00m,
        decimal? packageTare = 0.012m,
        bool isActive = true,
        decimal? target = null,
        decimal? lower = null,
        decimal? upper = null) =>
        new(id, code, name, unitPrice, packageTare, 30, "28", isActive, target, lower, upper);

    private static async Task<JsonElement> CreateAsync(WeighingTestApp app, object body)
    {
        var (response, json) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, json.ToString());
        return json;
    }

    private static async Task RejectedAsync(WeighingTestApp app, object body, string code)
    {
        var (response, json) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", body);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, code);
        json.GetProperty("code").GetString().ShouldBe(code);
    }

    private static string? Str(JsonElement json, string property) =>
        json.GetProperty(property).ValueKind == JsonValueKind.Null ? null : json.GetProperty(property).GetString();

    private static string[] ErrorKeys(JsonElement problem, string field) =>
        [.. problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!)];

    private static Dictionary<string, string?> Settings(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => $"Vyxum:Weighing:{v.Key}", v => (string?)v.Value);

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Saves_a_record_with_product_tare_amount_and_snapshot_and_publishes_the_event(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        await app.DeliverAsync(Product(Pistachio, name: "Pistachio, roasted", target: 1.25m, lower: 0.005m, upper: 0.01m));
        app.Scale.Set(Scale, 1.260m, "kg");
        var requestId = Guid.NewGuid();

        var (response, json) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", Body(requestId, productId: Pistachio, lot: " L-261121-03 "));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = json.GetProperty("id").GetInt64();
        response.Headers.Location!.ToString().ShouldBe($"/api/weighing/records/{id}");
        json.GetProperty("number").GetString().ShouldBe(id.ToString("D6", System.Globalization.CultureInfo.InvariantCulture));
        json.GetProperty("createdAt").GetDateTime().Kind.ShouldBe(DateTimeKind.Utc);
        Str(json, "deviceId").ShouldBe(Scale);
        json.GetProperty("productId").GetGuid().ShouldBe(Pistachio);
        Str(json, "productCode").ShouldBe("0417");
        Str(json, "productName").ShouldBe("Pistachio, roasted");
        Str(json, "lot").ShouldBe("L-261121-03");
        Str(json, "gross").ShouldBe("1.260");
        Str(json, "tare").ShouldBe("0.012");
        Str(json, "net").ShouldBe("1.248");
        Str(json, "unit").ShouldBe("kg");
        Str(json, "unitPrice").ShouldBe("780.00");
        Str(json, "amount").ShouldBe("973.44");
        Str(json, "currency").ShouldBe("TRY");
        Str(json, "status").ShouldBe("Saved");
        Str(json, "cancelledAt").ShouldBeNull();
        Str(json, "cancelReason").ShouldBeNull();
        Str(json, "operator").ShouldBeNull();
        Str(json, "targetWeight").ShouldBe("1.25");
        Str(json, "lowerTolerance").ShouldBe("0.005");
        Str(json, "upperTolerance").ShouldBe("0.01");

        var (got, gotJson) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records/{id}");
        got.StatusCode.ShouldBe(HttpStatusCode.OK);
        gotJson.ToString().ShouldBe(json.ToString());
        (await app.SendAsync(HttpMethod.Get, "/api/weighing/records/999999")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var message = (await app.OutboxAsync()).ShouldHaveSingleItem();
        message.EventName.ShouldBe("weighing.recorded");
        message.EventVersion.ShouldBe(1);
        var recorded = JsonSerializer.Deserialize<WeighingRecorded>(message.Payload, JsonSerializerOptions.Web)!;
        recorded.RecordId.ShouldBe(id);
        recorded.Number.ShouldBe(json.GetProperty("number").GetString());
        recorded.RequestId.ShouldBe(requestId);
        recorded.DeviceId.ShouldBe(Scale);
        recorded.ProductId.ShouldBe(Pistachio);
        recorded.ProductCode.ShouldBe("0417");
        recorded.Lot.ShouldBe("L-261121-03");
        recorded.Gross.ShouldBe(1.260m);
        recorded.Tare.ShouldBe(0.012m);
        recorded.Net.ShouldBe(1.248m);
        recorded.Unit.ShouldBe("kg");
        recorded.NetMilligrams.ShouldBe(1_248_000);
        recorded.UnitPrice.ShouldBe(780m);
        recorded.Amount.ShouldBe(973.44m);
        recorded.Currency.ShouldBe("TRY");
        recorded.TargetWeight.ShouldBe(1.25m);
        recorded.LowerTolerance.ShouldBe(0.005m);
        recorded.UpperTolerance.ShouldBe(0.01m);

        var stored = await app.WithDbAsync(db => Task.FromResult(db.Records.Single()));
        stored.NetMilligrams.ShouldBe(1_248_000);
        stored.AmountMinor.ShouldBe(97_344);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Gram_reading_with_kg_price_gives_the_right_amount_and_keeps_the_scale(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        await app.DeliverAsync(Product(Pistachio, unitPrice: 780.50m, packageTare: 0.0125m));
        await app.DeliverAsync(Product(Cashew, code: "0203", unitPrice: 1.25m, packageTare: null));

        // 572 g gross, 12.5 g package tare: net 559.5 g = 0.5595 kg x 780.50 = 436.69475 -> 436.69.
        app.Scale.Set(Scale, 572m, "G");
        var json = await CreateAsync(app, Body(productId: Pistachio));
        Str(json, "gross").ShouldBe("572");
        Str(json, "tare").ShouldBe("12.5");
        Str(json, "net").ShouldBe("559.5");
        Str(json, "unit").ShouldBe("g");
        Str(json, "amount").ShouldBe("436.69");

        // 500 g x 1.25 per kg = 0.625: the midpoint goes away from zero by default.
        app.Scale.Set(Scale, 500m, "g");
        var midpoint = await CreateAsync(app, Body(productId: Cashew));
        Str(midpoint, "gross").ShouldBe("500");
        Str(midpoint, "tare").ShouldBe("0");
        Str(midpoint, "net").ShouldBe("500");
        Str(midpoint, "amount").ShouldBe("0.63");

        var stored = await app.WithDbAsync(db => Task.FromResult(db.Records.OrderBy(r => r.Id).ToList()));
        stored[0].NetMilligrams.ShouldBe(559_500);
        stored[1].NetMilligrams.ShouldBe(500_000);
        stored[1].AmountMinor.ShouldBe(63);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Amount_rounding_currency_and_decimals_come_from_options(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider, settings: Settings(
            ("AmountRounding", "ToEven"), ("AmountDecimals", "1"), ("Currency", "EUR")));
        await app.DeliverAsync(Product(Cashew, code: "0203", unitPrice: 2.5m, packageTare: null));

        // 0.5 kg x 2.5 = 1.25 -> 1.2 with banker's rounding at one decimal.
        app.Scale.Set(Scale, 0.500m, "kg");
        var json = await CreateAsync(app, Body(productId: Cashew));
        Str(json, "amount").ShouldBe("1.2");
        Str(json, "currency").ShouldBe("EUR");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite, "mg", "500000", "0.5")]
    [InlineData(DatabaseProvider.Sqlite, "t", "0.0005", "0.5")]
    [InlineData(DatabaseProvider.Sqlite, "lb", "2", "0.90718474")]
    [InlineData(DatabaseProvider.Sqlite, "oz", "16", "0.45359237")]
    [InlineData(DatabaseProvider.Sqlite, "KG", "0.5", "0.5")]
    [InlineData(DatabaseProvider.SqlServer, "lb", "2", "0.90718474")]
    public async Task Converts_every_unit_to_kg_for_the_amount(DatabaseProvider provider, string unit, string reading, string kilograms)
    {
        await using var app = await WeighingTestApp.StartAsync(provider, settings: Settings(("AmountDecimals", "4")));
        await app.DeliverAsync(Product(Cashew, code: "0203", unitPrice: 10m, packageTare: null));
        var value = decimal.Parse(reading, System.Globalization.CultureInfo.InvariantCulture);
        app.Scale.Set(Scale, value, unit);

        var json = await CreateAsync(app, Body(productId: Cashew));

        var expected = decimal.Round(decimal.Parse(kilograms, System.Globalization.CultureInfo.InvariantCulture) * 10m, 4, MidpointRounding.AwayFromZero);
        Str(json, "amount").ShouldBe(expected.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
        Str(json, "unit").ShouldBe(unit.ToLowerInvariant());
        Str(json, "net").ShouldBe(reading);
        var stored = await app.WithDbAsync(db => Task.FromResult(db.Records.Single()));
        stored.NetMilligrams.ShouldBe((long)decimal.Round(decimal.Parse(kilograms, System.Globalization.CultureInfo.InvariantCulture) * 1_000_000m, 0, MidpointRounding.AwayFromZero));
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Explicit_tare_wins_over_package_tare_which_is_converted_to_the_scale_unit(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        await app.DeliverAsync(Product(Pistachio, unitPrice: null, packageTare: 0.012m));

        app.Scale.Set(Scale, 1.260m, "kg");
        var explicitTare = await CreateAsync(app, Body(productId: Pistachio, tare: "0.100"));
        Str(explicitTare, "tare").ShouldBe("0.100");
        Str(explicitTare, "net").ShouldBe("1.160");
        Str(explicitTare, "amount").ShouldBeNull();
        Str(explicitTare, "unitPrice").ShouldBeNull();

        var explicitZero = await CreateAsync(app, Body(productId: Pistachio, tare: "0"));
        Str(explicitZero, "tare").ShouldBe("0");
        Str(explicitZero, "net").ShouldBe("1.260");

        // Net keeps the larger number of decimals of gross and tare.
        var finer = await CreateAsync(app, Body(tare: "0.0125"));
        Str(finer, "gross").ShouldBe("1.260");
        Str(finer, "net").ShouldBe("1.2475");

        var packageTare = await CreateAsync(app, Body(productId: Pistachio));
        Str(packageTare, "tare").ShouldBe("0.012");
        Str(packageTare, "net").ShouldBe("1.248");

        app.Scale.Set(Scale, 1260m, "g");
        var inGrams = await CreateAsync(app, Body(productId: Pistachio));
        Str(inGrams, "tare").ShouldBe("12");
        Str(inGrams, "net").ShouldBe("1248");

        var noTare = await CreateAsync(app, Body());
        Str(noTare, "tare").ShouldBe("0");
        Str(noTare, "net").ShouldBe("1260");

        app.Scale.Set(Scale, 1.260m, "kg");
        var noTareKg = await CreateAsync(app, Body());
        Str(noTareKg, "tare").ShouldBe("0.000");
        Str(noTareKg, "net").ShouldBe("1.260");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_reading_the_scale_already_tared_is_not_tared_again(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        await app.DeliverAsync(Product(Pistachio, unitPrice: 100m, packageTare: 0.012m));

        // Net from the scale with its own tare: software tares (explicit or package) are ignored.
        app.Scale.Set(Scale, 1.000m, "kg", tare: 0.200m, isNet: true);
        var net = await CreateAsync(app, Body(productId: Pistachio, tare: "0.050"));
        Str(net, "gross").ShouldBe("1.200");
        Str(net, "tare").ShouldBe("0.200");
        Str(net, "net").ShouldBe("1.000");
        Str(net, "amount").ShouldBe("100.00");

        // Net flag without a reported tare.
        app.Scale.Set(Scale, 0.750m, "kg", isNet: true);
        var netOnly = await CreateAsync(app, Body(productId: Pistachio));
        Str(netOnly, "gross").ShouldBe("0.750");
        Str(netOnly, "tare").ShouldBe("0.000");
        Str(netOnly, "net").ShouldBe("0.750");

        // Gross with a reported tare.
        app.Scale.Set(Scale, 1.200m, "kg", tare: 0.200m, isNet: false);
        var gross = await CreateAsync(app, Body(productId: Pistachio));
        Str(gross, "gross").ShouldBe("1.200");
        Str(gross, "tare").ShouldBe("0.200");
        Str(gross, "net").ShouldBe("1.000");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Readings_that_break_a_rule_are_rejected_with_a_code(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        await app.DeliverAsync(Product(Pistachio));
        await app.DeliverAsync(Product(Cashew, code: "0203", isActive: false));

        await RejectedAsync(app, Body(deviceId: "unknown"), WeighingErrors.DeviceNotConnected);

        app.Scale.SetWithoutReading(Scale);
        await RejectedAsync(app, Body(), WeighingErrors.NoReading);

        app.Scale.Set(Scale, 1m, connection: DeviceConnectionState.Disconnected);
        await RejectedAsync(app, Body(), WeighingErrors.DeviceNotConnected);

        app.Scale.Set(Scale, 1m, connection: DeviceConnectionState.Connecting);
        await RejectedAsync(app, Body(), WeighingErrors.DeviceNotConnected);

        app.Scale.Set(Scale, null, state: LiveWeightState.Overload);
        await RejectedAsync(app, Body(), WeighingErrors.Overload);

        app.Scale.Set(Scale, null, state: LiveWeightState.Underload);
        await RejectedAsync(app, Body(), WeighingErrors.Underload);

        app.Scale.Set(Scale, 1.000m, state: LiveWeightState.Motion);
        await RejectedAsync(app, Body(), WeighingErrors.NotStable);

        app.Scale.Set(Scale, null);
        await RejectedAsync(app, Body(), WeighingErrors.NoReading);

        app.Scale.Set(Scale, 0.000m);
        await RejectedAsync(app, Body(), WeighingErrors.NetTooSmall);

        app.Scale.Set(Scale, -0.010m);
        await RejectedAsync(app, Body(), WeighingErrors.NetTooSmall);

        app.Scale.Set(Scale, 0.500m);
        await RejectedAsync(app, Body(tare: "0.500"), WeighingErrors.NetTooSmall);
        await RejectedAsync(app, Body(productId: Guid.NewGuid()), WeighingErrors.UnknownProduct);
        await RejectedAsync(app, Body(productId: Cashew), WeighingErrors.InactiveProduct);

        // Package tare and price need the scale's unit.
        app.Scale.Set(Scale, 0.500m, unit: null);
        await RejectedAsync(app, Body(productId: Pistachio), WeighingErrors.UnknownUnit);
        app.Scale.Set(Scale, 0.500m, unit: "pcs");
        await RejectedAsync(app, Body(productId: Pistachio, tare: "0.010"), WeighingErrors.UnknownUnit);

        (await app.OutboxAsync()).ShouldBeEmpty();
        (await app.WithDbAsync(db => Task.FromResult(db.Records.Count()))).ShouldBe(0);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_product_less_record_works_without_a_known_unit(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);

        app.Scale.Set(Scale, 0.500m, unit: null);
        var noUnit = await CreateAsync(app, Body(tare: "0.100"));
        Str(noUnit, "unit").ShouldBeNull();
        Str(noUnit, "net").ShouldBe("0.400");
        Str(noUnit, "amount").ShouldBeNull();

        app.Scale.Set(Scale, 3m, unit: "pcs");
        var otherUnit = await CreateAsync(app, Body());
        Str(otherUnit, "unit").ShouldBe("pcs");

        var stored = await app.WithDbAsync(db => Task.FromResult(db.Records.OrderBy(r => r.Id).ToList()));
        stored.ShouldAllBe(r => r.NetMilligrams == null && r.AmountMinor == null);

        var (_, list) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records");
        var net = list.GetProperty("totals").GetProperty("net").EnumerateArray().ToList();
        net.Count.ShouldBe(2);
        Str(net[0], "unit").ShouldBeNull();
        Str(net[0], "value").ShouldBe("0.400");
        Str(net[1], "unit").ShouldBe("pcs");
        Str(net[1], "value").ShouldBe("3");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Stability_and_minimum_net_come_from_options(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider, settings: Settings(("RequireStable", "false"), ("MinimumNet", "0.5")));

        app.Scale.Set(Scale, 0.499m, state: LiveWeightState.Motion);
        await RejectedAsync(app, Body(), WeighingErrors.NetTooSmall);

        app.Scale.Set(Scale, 499m, "g", state: LiveWeightState.Motion);
        await RejectedAsync(app, Body(), WeighingErrors.NetTooSmall);

        app.Scale.Set(Scale, 0.500m, unit: null);
        await RejectedAsync(app, Body(), WeighingErrors.UnknownUnit);

        app.Scale.Set(Scale, 500m, "g", state: LiveWeightState.Motion);
        await CreateAsync(app, Body());

        var (_, settings) = await app.SendAsync(HttpMethod.Get, "/api/weighing/settings");
        settings.GetProperty("requireStable").GetBoolean().ShouldBeFalse();
        Str(settings, "minimumNet").ShouldBe("0.5");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_reading_older_than_the_limit_is_not_recorded(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);

        // The agent stopped sending, but the connection still looks open.
        app.Scale.Set(Scale, 1.250m, age: TimeSpan.FromSeconds(4));
        await RejectedAsync(app, Body(), WeighingErrors.NoReading);

        app.Scale.Set(Scale, 1.250m, age: TimeSpan.FromSeconds(2));
        await CreateAsync(app, Body());
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_save_carrying_the_seen_reading_is_rejected_when_the_scale_shows_another_weight(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        app.Scale.Set(Scale, 2.40m);
        var seen = app.Scale.GetLatest(Scale)!;
        var requestId = Guid.NewGuid();
        object WithSeen() => new { requestId, deviceId = Scale, reading = new { value = "2.400", unit = "kg", receivedAt = seen.ReceivedAt } };

        // The first try never reached the server; the operator put the next crate on the scale meanwhile.
        app.Scale.Set(Scale, 2.95m);
        await RejectedAsync(app, WithSeen(), WeighingErrors.ReadingChanged);

        // Same weight again (trailing zeros do not matter) but the save comes too late to be sure.
        app.Clock.Advance(TimeSpan.FromSeconds(11));
        app.Scale.Set(Scale, 2.4m);
        await RejectedAsync(app, WithSeen(), WeighingErrors.ReadingChanged);

        // A unit change is a different reading as well.
        app.Scale.Set(Scale, 2.4m, "lb");
        await RejectedAsync(app, new { requestId, deviceId = Scale, reading = new { value = "2.4", unit = "kg", receivedAt = app.Clock.GetUtcNow() } }, WeighingErrors.ReadingChanged);

        // In time and unchanged: saved with the weight the operator saw.
        app.Scale.Set(Scale, 2.4m);
        var json = await CreateAsync(app, new { requestId, deviceId = Scale, reading = new { value = "2.400", unit = "KG", receivedAt = app.Clock.GetUtcNow() } });
        Str(json, "gross").ShouldBe("2.4");

        // A repeat of a saved request still returns the first record, whatever the scale shows now.
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        app.Scale.Set(Scale, 7m);
        var (response, again) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", WithSeen());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.GetProperty("id").GetInt64().ShouldBe(json.GetProperty("id").GetInt64());
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_invalid_seen_reading_is_a_validation_problem(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        app.Scale.Set(Scale, 1m);

        var (response, problem) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records",
            new { requestId = Guid.NewGuid(), deviceId = Scale, reading = new { value = "1,0", unit = "kg" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(problem, "reading.value").ShouldBe([WeighingErrors.InvalidNumber]);
        ErrorKeys(problem, "reading.receivedAt").ShouldBe([WeighingErrors.InvalidNumber]);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task The_reading_age_limit_comes_from_options(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider, settings: Settings(("MaxReadingAge", "00:00:10")));
        app.Scale.Set(Scale, 1.250m, age: TimeSpan.FromSeconds(8));
        await CreateAsync(app, Body());

        await using var off = await WeighingTestApp.StartAsync(provider, settings: Settings(("MaxReadingAge", "00:00:00")));
        off.Scale.Set(Scale, 1.250m, age: TimeSpan.FromMinutes(5));
        await CreateAsync(off, Body());
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Invalid_requests_are_validation_problems_with_resx_keys(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        app.Scale.Set(Scale, 1m);

        var (response, json) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", new
        {
            requestId = Guid.Empty,
            deviceId = " ",
            lot = new string('x', 65),
            tare = "1,5",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(json, "requestId").ShouldBe([WeighingErrors.RequestIdRequired]);
        ErrorKeys(json, "deviceId").ShouldBe([WeighingErrors.DeviceIdRequired]);
        ErrorKeys(json, "lot").ShouldBe([WeighingErrors.LotTooLong]);
        ErrorKeys(json, "tare").ShouldBe([WeighingErrors.InvalidNumber]);

        foreach (var (tare, key) in new[] { ("-0.1", WeighingErrors.NegativeNotAllowed), ("1e3", WeighingErrors.InvalidNumber), ("0.0000001", WeighingErrors.InvalidNumber) })
        {
            var (bad, badJson) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", Body(tare: tare));
            bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            ErrorKeys(badJson, "tare").ShouldBe([key]);
        }

        var (missing, missingJson) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", new { deviceId = Scale });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(missingJson, "requestId").ShouldBe([WeighingErrors.RequestIdRequired]);

        (await app.OutboxAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_repeated_request_id_returns_the_first_record_and_saves_once(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        app.Scale.Set(Scale, 1.248m);
        var requestId = Guid.NewGuid();

        var first = await CreateAsync(app, Body(requestId));

        // The scale changed (even to an invalid state) since the first attempt: the retry still gets the saved record.
        app.Scale.Set(Scale, 2.000m, state: LiveWeightState.Motion);
        var (again, againJson) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", Body(requestId));
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        againJson.ToString().ShouldBe(first.ToString());

        // Concurrent duplicates: one record, everyone gets it.
        app.Scale.Set(Scale, 0.500m);
        var raceId = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => app.SendAsync(HttpMethod.Post, "/api/weighing/records", Body(raceId))));
        responses.Count(r => r.Response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.ShouldAllBe(r => r.Response.StatusCode == HttpStatusCode.Created || r.Response.StatusCode == HttpStatusCode.OK);
        responses.Select(r => r.Body.GetProperty("id").GetInt64()).Distinct().Count().ShouldBe(1);

        (await app.WithDbAsync(db => Task.FromResult(db.Records.Count()))).ShouldBe(2);
        (await app.OutboxAsync()).Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Cancel_needs_a_reason_happens_once_and_publishes_the_event(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        app.Scale.Set(Scale, 1.248m);
        var id = (await CreateAsync(app, Body())).GetProperty("id").GetInt64();

        var (noReason, noReasonJson) = await app.SendAsync(HttpMethod.Post, $"/api/weighing/records/{id}/cancel", new { reason = "  " });
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(noReasonJson, "reason").ShouldBe([WeighingErrors.CancelReasonRequired]);

        var (tooLong, tooLongJson) = await app.SendAsync(HttpMethod.Post, $"/api/weighing/records/{id}/cancel", new { reason = new string('x', 501) });
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ErrorKeys(tooLongJson, "reason").ShouldBe([WeighingErrors.CancelReasonTooLong]);

        (await app.SendAsync(HttpMethod.Post, "/api/weighing/records/999999/cancel", new { reason = "x" })).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var (cancelled, cancelledJson) = await app.SendAsync(HttpMethod.Post, $"/api/weighing/records/{id}/cancel", new { reason = " Wrong product " });
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        Str(cancelledJson, "status").ShouldBe("Cancelled");
        Str(cancelledJson, "cancelReason").ShouldBe("Wrong product");
        cancelledJson.GetProperty("cancelledAt").GetDateTime().Kind.ShouldBe(DateTimeKind.Utc);

        var (twice, twiceJson) = await app.SendAsync(HttpMethod.Post, $"/api/weighing/records/{id}/cancel", new { reason = "Again" });
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        Str(twiceJson, "code").ShouldBe(WeighingErrors.AlreadyCancelled);

        var messages = await app.OutboxAsync();
        messages.Select(m => m.EventName).ShouldBe(["weighing.recorded", "weighing.cancelled"]);
        var cancelledEvent = JsonSerializer.Deserialize<WeighingCancelled>(messages[1].Payload, JsonSerializerOptions.Web)!;
        cancelledEvent.RecordId.ShouldBe(id);
        cancelledEvent.Reason.ShouldBe("Wrong product");

        var (got, gotJson) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records/{id}");
        got.StatusCode.ShouldBe(HttpStatusCode.OK);
        Str(gotJson, "status").ShouldBe("Cancelled");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Cancel_reason_is_optional_when_configured(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider, settings: Settings(("RequireCancelReason", "false")));
        app.Scale.Set(Scale, 1.248m);
        var id = (await CreateAsync(app, Body())).GetProperty("id").GetInt64();

        var (cancelled, json) = await app.SendAsync(HttpMethod.Post, $"/api/weighing/records/{id}/cancel", new { reason = (string?)null });
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        Str(json, "cancelReason").ShouldBeNull();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task List_filters_pages_and_totals_ignore_cancelled_records(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        await app.DeliverAsync(Product(Pistachio, unitPrice: 780m, packageTare: null));
        await app.DeliverAsync(Product(Cashew, code: "0203", unitPrice: 640m, packageTare: null));
        var start = app.Clock.GetUtcNow().UtcDateTime;

        async Task<long> Weigh(decimal value, string unit, Guid? product, string? lot)
        {
            app.Scale.Set(Scale, value, unit);
            var json = await CreateAsync(app, Body(productId: product, lot: lot));
            app.Clock.Advance(TimeSpan.FromHours(1));
            return json.GetProperty("id").GetInt64();
        }

        var a = await Weigh(0.512m, "kg", Cashew, "L-2");      // 327.68
        var b = await Weigh(0.498m, "kg", Cashew, "L-2");      // 318.72, cancelled below
        var c = await Weigh(1.2475m, "kg", Pistachio, "L-3");  // 973.05
        var d = await Weigh(1248m, "g", Pistachio, "L-3");     // 973.44
        var e = await Weigh(0.25m, "kg", null, null);          // no amount
        (await app.SendAsync(HttpMethod.Post, $"/api/weighing/records/{b}/cancel", new { reason = "Wrong" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var (response, all) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ShouldBe([e, d, c, b, a]);
        var totals = all.GetProperty("totals");
        totals.GetProperty("validCount").GetInt32().ShouldBe(4);
        totals.GetProperty("cancelledCount").GetInt32().ShouldBe(1);
        var net = totals.GetProperty("net").EnumerateArray().Select(n => $"{Str(n, "unit")}={Str(n, "value")}").ToList();
        net.ShouldBe(["g=1248", "kg=2.0095"]);
        Str(totals, "amount").ShouldBe("2274.17");

        var (_, valid) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records?includeCancelled=false");
        valid.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ShouldBe([e, d, c, a]);
        valid.GetProperty("totals").GetProperty("cancelledCount").GetInt32().ShouldBe(1);

        var (_, cashew) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records?productId={Cashew}");
        cashew.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ShouldBe([b, a]);
        Str(cashew.GetProperty("totals"), "amount").ShouldBe("327.68");
        cashew.GetProperty("totals").GetProperty("net").EnumerateArray().Single().GetProperty("value").GetString().ShouldBe("0.512");

        var (_, lot) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records?lot=L-3");
        lot.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ShouldBe([d, c]);

        // From inclusive, to exclusive, UTC.
        var from = Uri.EscapeDataString(start.AddHours(1).ToString("O"));
        var to = Uri.EscapeDataString(start.AddHours(3).ToString("O"));
        var (_, range) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records?from={from}&to={to}");
        range.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ShouldBe([c, b]);
        range.GetProperty("totals").GetProperty("validCount").GetInt32().ShouldBe(1);

        var (_, page) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records?skip=1&take=2");
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ShouldBe([d, c]);
        page.GetProperty("totals").GetProperty("validCount").GetInt32().ShouldBe(4);

        var (_, huge) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records?take=100000");
        huge.GetProperty("items").GetArrayLength().ShouldBe(5);

        var (_, none) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records?productId={Guid.NewGuid()}");
        none.GetProperty("items").GetArrayLength().ShouldBe(0);
        none.GetProperty("totals").GetProperty("net").GetArrayLength().ShouldBe(0);
        Str(none.GetProperty("totals"), "amount").ShouldBeNull();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Recent_records_are_per_device_and_newest_first(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        app.Scale.Set(Scale, 1m);
        app.Scale.Set("scale-2", 2m);

        var ids = new List<long>();
        for (var i = 0; i < 12; i++)
        {
            ids.Add((await CreateAsync(app, Body())).GetProperty("id").GetInt64());
        }

        var other = (await CreateAsync(app, Body(deviceId: "scale-2"))).GetProperty("id").GetInt64();

        var (_, recent) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records/recent?deviceId={Scale}");
        recent.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ShouldBe(Enumerable.Reverse(ids).Take(10));

        var (_, three) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records/recent?deviceId={Scale}&take=3");
        three.GetArrayLength().ShouldBe(3);

        var (_, second) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records/recent?deviceId=scale-2");
        second.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ShouldBe([other]);

        var (_, any) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records/recent?take=2");
        any.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ShouldBe([other, ids[^1]]);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Records_keep_the_target_and_tolerances_from_save_time_and_the_products_endpoint_shows_the_current_ones(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        var t0 = DateTime.UtcNow;
        await app.DeliverAsync(Product(Pistachio, target: 1.25m, lower: 0.005m, upper: 0.01m), t0);
        app.Scale.Set(Scale, 1.260m, "kg");
        var withProduct = await CreateAsync(app, Body(productId: Pistachio));
        var withoutProduct = await CreateAsync(app, Body());
        Str(withoutProduct, "targetWeight").ShouldBeNull();
        Str(withoutProduct, "lowerTolerance").ShouldBeNull();
        Str(withoutProduct, "upperTolerance").ShouldBeNull();

        // The product changes later; the old record keeps what was valid when it was saved.
        await app.DeliverAsync(Product(Pistachio, target: 0.5m, lower: 0.01m, upper: null), t0.AddSeconds(1));
        var newer = await CreateAsync(app, Body(productId: Pistachio));

        var (_, old) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records/{withProduct.GetProperty("id").GetInt64()}");
        Str(old, "targetWeight").ShouldBe("1.25");
        Str(old, "lowerTolerance").ShouldBe("0.005");
        Str(old, "upperTolerance").ShouldBe("0.01");
        Str(newer, "targetWeight").ShouldBe("0.50");
        Str(newer, "lowerTolerance").ShouldBe("0.01");
        Str(newer, "upperTolerance").ShouldBeNull();

        var (_, products) = await app.SendAsync(HttpMethod.Get, "/api/weighing/products");
        var current = products.EnumerateArray().Single();
        Str(current, "targetWeight").ShouldBe("0.50");
        Str(current, "upperTolerance").ShouldBeNull();

        var stored = await app.WithDbAsync(db => Task.FromResult(db.Records.OrderBy(r => r.Id).ToList()));
        stored[0].TargetWeight.ShouldBe(1.25m);
        stored[1].TargetWeight.ShouldBeNull();
        stored[2].TargetWeight.ShouldBe(0.5m);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Product_events_upsert_the_local_copy_idempotently(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);
        var t0 = DateTime.UtcNow;

        await app.DeliverAsync(Product(Pistachio, name: "Pistachio"), t0);
        await app.DeliverAsync(Product(Cashew, code: "0203", name: "Cashew"), t0);
        await app.DeliverAsync(Product(Guid.NewGuid(), code: "0110", name: "Chickpeas", isActive: false), t0);

        var (_, list) = await app.SendAsync(HttpMethod.Get, "/api/weighing/products");
        list.EnumerateArray().Select(p => p.GetProperty("code").GetString()).ShouldBe(["0203", "0417"]);
        var pistachio = list.EnumerateArray().Last();
        pistachio.GetProperty("id").GetGuid().ShouldBe(Pistachio);
        Str(pistachio, "name").ShouldBe("Pistachio");
        Str(pistachio, "unitPrice").ShouldBe("780.00");
        Str(pistachio, "packageTare").ShouldBe("0.012");
        pistachio.GetProperty("shelfLifeDays").GetInt32().ShouldBe(30);
        pistachio.GetProperty("targetWeight").ValueKind.ShouldBe(JsonValueKind.Null);
        Str(pistachio, "barcodePrefix").ShouldBe("28");

        // A newer event replaces the copy; delivering it again changes nothing; an older one is ignored.
        var changed = Product(Pistachio, name: "Pistachio, roasted", unitPrice: 825.5m, packageTare: null, target: 0.25m, lower: 0.005m, upper: 0.01m);
        await app.DeliverAsync(changed, t0.AddSeconds(1));
        await app.DeliverAsync(changed, t0.AddSeconds(1));
        await app.DeliverAsync(Product(Pistachio, name: "Stale"), t0.AddMilliseconds(500));

        var copy = await app.WithDbAsync(db => Task.FromResult(db.Products.Single(p => p.ProductId == Pistachio)));
        copy.Name.ShouldBe("Pistachio, roasted");
        copy.UnitPrice.ShouldBe(825.5m);
        copy.PackageTare.ShouldBeNull();
        copy.TargetWeight.ShouldBe(0.25m);
        copy.LowerTolerance.ShouldBe(0.005m);
        copy.UpperTolerance.ShouldBe(0.01m);
        (await app.WithDbAsync(db => Task.FromResult(db.Products.Count()))).ShouldBe(3);

        var (_, search) = await app.SendAsync(HttpMethod.Get, "/api/weighing/products?search=ROAST");
        search.EnumerateArray().Select(p => p.GetProperty("code").GetString()).ShouldBe(["0417"]);
        var (_, byCode) = await app.SendAsync(HttpMethod.Get, "/api/weighing/products?search=20");
        byCode.EnumerateArray().Select(p => p.GetProperty("code").GetString()).ShouldBe(["0203"]);

        await app.DeliverAsync(Product(Cashew, code: "0203", name: "Cashew", isActive: false), t0.AddSeconds(2));
        var (_, afterDeactivate) = await app.SendAsync(HttpMethod.Get, "/api/weighing/products");
        afterDeactivate.EnumerateArray().Select(p => p.GetProperty("code").GetString()).ShouldBe(["0417"]);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Works_without_the_products_module(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider);

        var (response, settings) = await app.SendAsync(HttpMethod.Get, "/api/weighing/settings");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        settings.GetProperty("productsAvailable").GetBoolean().ShouldBeFalse();
        settings.GetProperty("canPrintLabel").GetBoolean().ShouldBeFalse();
        settings.GetProperty("showOperator").GetBoolean().ShouldBeFalse();
        settings.GetProperty("requireStable").GetBoolean().ShouldBeTrue();
        Str(settings, "minimumNet").ShouldBe("0");
        settings.GetProperty("requireCancelReason").GetBoolean().ShouldBeTrue();
        Str(settings, "currency").ShouldBe("TRY");
        settings.GetProperty("amountDecimals").GetInt32().ShouldBe(2);

        (await app.SendAsync(HttpMethod.Get, "/api/weighing/products")).Body.GetArrayLength().ShouldBe(0);
        (await app.SendAsync(HttpMethod.Get, "/api/products")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        app.Scale.Set(Scale, 1.248m);
        var json = await CreateAsync(app, Body());
        Str(json, "productId").ShouldBeNull();
        Str(json, "net").ShouldBe("1.248");
        await RejectedAsync(app, Body(productId: Pistachio), WeighingErrors.UnknownProduct);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Products_works_without_the_weighing_module(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider, withProducts: true, withWeighing: false);

        var (created, _) = await app.SendAsync(HttpMethod.Post, "/api/products", new { code = "0417", name = "Pistachio", unitPrice = "780" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await app.DispatchAsync()).ShouldBe(1);
        (await app.SendAsync(HttpMethod.Get, "/api/products")).Body.GetArrayLength().ShouldBe(1);
        (await app.SendAsync(HttpMethod.Get, "/api/weighing/settings")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task End_to_end_product_from_the_catalog_is_weighed_and_cancelled(DatabaseProvider provider)
    {
        await using var app = await WeighingTestApp.StartAsync(provider, withProducts: true);
        (await app.SendAsync(HttpMethod.Get, "/api/weighing/settings")).Body.GetProperty("productsAvailable").GetBoolean().ShouldBeTrue();

        var (created, product) = await app.SendAsync(HttpMethod.Post, "/api/products", new
        {
            code = "0417",
            name = "Pistachio, roasted",
            unitPrice = "780.00",
            packageTare = "0.012",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var productId = product.GetProperty("id").GetGuid();

        // Not in Weighing's copy until the event is delivered.
        (await app.SendAsync(HttpMethod.Get, "/api/weighing/products")).Body.GetArrayLength().ShouldBe(0);
        await app.DispatchAsync();
        var (_, copy) = await app.SendAsync(HttpMethod.Get, "/api/weighing/products?search=0417");
        var listed = copy.EnumerateArray().ShouldHaveSingleItem();
        listed.GetProperty("id").GetGuid().ShouldBe(productId);
        Str(listed, "unitPrice").ShouldBe("780.00");
        Str(listed, "packageTare").ShouldBe("0.012");

        app.Scale.Set(Scale, 1.260m, "kg");
        var record = await CreateAsync(app, Body(productId: productId, lot: "L-261121-03"));
        Str(record, "net").ShouldBe("1.248");
        Str(record, "amount").ShouldBe("973.44");
        var id = record.GetProperty("id").GetInt64();

        // A later price change reaches the copy but not the saved record.
        (await app.SendAsync(HttpMethod.Put, $"/api/products/{productId}", new { code = "0417", name = "Pistachio", unitPrice = "900", packageTare = "0.012" }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await app.DispatchAsync();
        Str((await app.SendAsync(HttpMethod.Get, "/api/weighing/products")).Body.EnumerateArray().Single(), "unitPrice").ShouldBe("900.00");
        var (_, saved) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records/{id}");
        Str(saved, "unitPrice").ShouldBe("780.00");
        Str(saved, "productName").ShouldBe("Pistachio, roasted");

        var (cancelled, _) = await app.SendAsync(HttpMethod.Post, $"/api/weighing/records/{id}/cancel", new { reason = "Wrong lot" });
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);

        var (_, list) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records");
        Str(list.GetProperty("items").EnumerateArray().Single(), "status").ShouldBe("Cancelled");
        list.GetProperty("totals").GetProperty("validCount").GetInt32().ShouldBe(0);
        list.GetProperty("totals").GetProperty("cancelledCount").GetInt32().ShouldBe(1);
        Str(list.GetProperty("totals"), "amount").ShouldBeNull();

        // Deactivating the product removes it from the weighing list.
        (await app.SendAsync(HttpMethod.Post, $"/api/products/{productId}/deactivate")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await app.DispatchAsync();
        (await app.SendAsync(HttpMethod.Get, "/api/weighing/products")).Body.GetArrayLength().ShouldBe(0);
        await RejectedAsync(app, Body(productId: productId), WeighingErrors.InactiveProduct);

        // Weighing's own events have no handler and are marked processed.
        (await app.OutboxAsync()).ShouldAllBe(m => m.ProcessedAt != null || m.EventName != "weighing.recorded");
    }

    [Fact]
    public void Module_manifest_and_strings_are_complete()
    {
        var manifest = new WeighingModule().Manifest;
        manifest.Id.ShouldBe("weighing");
        manifest.Provides.ShouldBe([(Capability)"weighing.records"]);
        manifest.Optional.ShouldBe([(Capability)"products.catalog", (Capability)"labels.print", (Capability)"identity.users"]);
        manifest.Publishes.ShouldBe([typeof(WeighingRecorded), typeof(WeighingCancelled)]);
        IntegrationEventAttribute.Find(typeof(WeighingRecorded))!.ToString().ShouldBe("weighing.recorded v1");
        IntegrationEventAttribute.Find(typeof(WeighingCancelled))!.ToString().ShouldBe("weighing.cancelled v1");
        IntegrationEventAttribute.Find(typeof(ProductSaved))!.ToString().ShouldBe("products.product-saved v1");

        // An event written before the target fields existed still reads: they come back empty.
        var old = JsonSerializer.Deserialize<ProductSaved>(
            """{"productId":"0e7d3f7a-5d0e-4a39-9f0e-000000000417","code":"0417","name":"Pistachio","unitPrice":780.0,"packageTare":null,"shelfLifeDays":30,"barcodePrefix":"28","isActive":true}""",
            JsonSerializerOptions.Web)!;
        old.TargetWeight.ShouldBeNull();
        old.LowerTolerance.ShouldBeNull();
        old.UpperTolerance.ShouldBeNull();

        // The local event copy matches the Products module's contract field by field.
        typeof(ProductSaved).GetProperties().Select(p => $"{p.Name}:{p.PropertyType}")
            .ShouldBe(typeof(Vyxum.Modules.Products.ProductSaved).GetProperties().Select(p => $"{p.Name}:{p.PropertyType}"));

        var english = new System.Globalization.CultureInfo("en");
        var turkish = new System.Globalization.CultureInfo("tr");
        var errorKeys = typeof(WeighingErrors).GetFields().Select(f => ((string)f.GetValue(null)!)["weighing.".Length..]);
        foreach (var key in errorKeys.Concat(
        [
            "Title", "RecordsTitle", "Net", "Gross", "Tare", "Amount", "PricePerKg", "SaveRecord", "SaveAndPrint", "TakeTare", "ClearTare",
            "Zero", "ProductSearch", "NoProduct", "Lot", "RecentRecords", "AllRecords", "Saved", "Cancelled", "CancelRecord", "CancelReason",
            "CancelConfirm", "Date", "Time", "RecordNo", "Plu", "Product", "Operator", "Status", "AllProducts", "ExportExcel", "ValidSummary",
            "CancelledNotInTotals", "Device", "SelectDevice", "Saving", "SaveFailed", "Retrying", "ZeroDone", "ZeroNotSupported",
            "ZeroRejected", "ZeroUnavailable", "Total", "TargetWeight", "LowerTolerance", "UpperTolerance",
        ]))
        {
            WeighingStrings.Get(key, english).ShouldNotBeNullOrWhiteSpace(key);
            WeighingStrings.Get(key, turkish).ShouldNotBeNullOrWhiteSpace(key);
        }

        WeighingStrings.Get("Title", turkish).ShouldBe("Tartım");
        WeighingStrings.Get("RecordsTitle", turkish).ShouldBe("Tartım kayıtları");
        WeighingStrings.Get("SaveRecord", turkish).ShouldBe("Tartımı kaydet");
        WeighingStrings.Get("ProductSearch", turkish).ShouldBe("Ürün, PLU ya da ad");
        WeighingStrings.Get("ValidSummary", turkish).ShouldBe("{0} geçerli kayıt, {1} iptal");
        WeighingStrings.Get("Title", english).ShouldBe("Weighing");
    }
}
