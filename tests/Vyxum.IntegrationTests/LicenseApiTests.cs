using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Vyxum.Samples.Notifications;
using Vyxum.Samples.Orders;
using Vyxum.Samples.Paid;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>
/// Licensing in the running application (phase F4): <c>/api/license</c>, the gate on a paid module's endpoints, the
/// inbox gate, the station choice and <c>/api/ui/modules</c>, on both providers.
/// </summary>
public sealed class LicenseApiTests : IDisposable
{
    private const string Entries = "/api/samples-paid/entries";

    private readonly TestLicenseIssuer _issuer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _issuer.Dispose();

    private LicenseTestApp Start(TestDatabase database, TestClock? clock = null) => new(database, _issuer, clock);

    private static void ShouldBeProblem((int Status, JsonElement Body) response, int status, string code)
    {
        response.Status.ShouldBe(status);
        response.Body.GetProperty("code").GetString().ShouldBe(code);
    }

    private static async Task ShouldUploadAsync(LicenseTestApp app, string file)
    {
        using var response = await app.UploadAsync(file);
        var (status, body) = await LicenseTestApp.ReadAsync(response);
        status.ShouldBe(200, body.ToString());
    }

    private static async Task<(int Status, JsonElement Body)> UploadAsync(LicenseTestApp app, string file)
    {
        using var response = await app.UploadAsync(file);
        return await LicenseTestApp.ReadAsync(response);
    }

    private static Task<(int Status, JsonElement Body)> PostEntryAsync(LicenseTestApp app) =>
        app.SendAsync(HttpMethod.Post, Entries, """{ "text": "manual" }""");

    private static async Task<T> QueryAsync<TContext, T>(LicenseTestApp app, Func<TContext, Task<T>> query)
        where TContext : DbContext
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    private static async Task PlaceOrderAsync(LicenseTestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrderService>().PlaceOrderAsync("Flour", 2, Ct);
    }

    private static Task<List<string>> PaidEntriesAsync(LicenseTestApp app) =>
        QueryAsync<PaidDbContext, List<string>>(app, db => db.Entries.Select(e => e.Text).ToListAsync(Ct));

    private static Task<OutboxMessage> SingleOutboxMessageAsync(LicenseTestApp app) =>
        QueryAsync<OrdersDbContext, OutboxMessage>(app, db => db.OutboxMessages.SingleAsync(Ct));

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Without_a_license_the_paid_module_is_locked_and_free_modules_work(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = Start(database);

        ShouldBeProblem(await app.SendAsync(HttpMethod.Get, Entries), 403, "license_required");
        ShouldBeProblem(await PostEntryAsync(app), 403, "license_required");
        ShouldBeProblem(await app.SendAsync(HttpMethod.Get, "/api/samples-paid/stations/scale-1"), 403, "license_required");
        ShouldBeProblem(await app.SendAsync(HttpMethod.Post, "/hubs/samples-paid/negotiate?negotiateVersion=1"), 403, "license_required");
        (await app.SendAsync(HttpMethod.Get, "/api/products")).Status.ShouldBe(200);
        (await app.SendAsync(HttpMethod.Get, "/health")).Status.ShouldBe(200);

        var (status, license) = await app.GetLicenseAsync();
        status.ShouldBe(200);
        license.GetProperty("hasLicense").GetBoolean().ShouldBeFalse();
        license.GetProperty("valid").GetBoolean().ShouldBeFalse();
        license.GetProperty("status").GetString().ShouldBe("none");
        license.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        license.GetProperty("stationLimit").GetInt32().ShouldBe(0);
        var module = license.GetProperty("modules").EnumerateArray().ShouldHaveSingleItem();
        module.GetProperty("id").GetString().ShouldBe(PaidModule.Id);
        module.GetProperty("installed").GetBoolean().ShouldBeTrue();
        module.GetProperty("licensed").GetBoolean().ShouldBeFalse();
        module.GetProperty("license").GetString().ShouldBe("locked");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_valid_license_unlocks_the_paid_module(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = Start(database);
        var payload = TestLicenseIssuer.Payload(modules: [PaidModule.Id, "samples.not-installed"]);

        var (status, license) = await UploadAsync(app, _issuer.Sign(payload));

        status.ShouldBe(200);
        license.GetProperty("hasLicense").GetBoolean().ShouldBeTrue();
        license.GetProperty("valid").GetBoolean().ShouldBeTrue();
        license.GetProperty("status").GetString().ShouldBe("valid");
        license.GetProperty("licenseId").GetString().ShouldBe("lic-1");
        license.GetProperty("type").GetString().ShouldBe("subscription");
        license.GetProperty("licensee").GetProperty("name").GetString().ShouldBe("Test Gıda A.Ş.");
        license.GetProperty("validFrom").GetString().ShouldBe(payload.ValidFrom.ToString("yyyy-MM-dd"));
        license.GetProperty("validUntil").GetString().ShouldBe(payload.ValidUntil!.Value.ToString("yyyy-MM-dd"));
        license.GetProperty("graceDaysLeft").ValueKind.ShouldBe(JsonValueKind.Null);
        license.GetProperty("clockBehind").GetBoolean().ShouldBeFalse();
        license.GetProperty("stationLimit").GetInt32().ShouldBe(2);
        var modules = license.GetProperty("modules").EnumerateArray()
            .Select(m => (m.GetProperty("id").GetString(), m.GetProperty("installed").GetBoolean(), m.GetProperty("licensed").GetBoolean(), m.GetProperty("license").GetString()))
            .ToList();
        modules.ShouldBe([("samples.not-installed", false, true, "licensed"), (PaidModule.Id, true, true, "licensed")]);

        (await app.SendAsync(HttpMethod.Get, Entries)).Status.ShouldBe(200);
        (await PostEntryAsync(app)).Status.ShouldBe(201);
        Strings((await app.SendAsync(HttpMethod.Get, Entries)).Body).ShouldBe(["manual"]);
        (await app.SendAsync(HttpMethod.Post, "/hubs/samples-paid/negotiate?negotiateVersion=1")).Status.ShouldBe(200);
        app.Services.GetRequiredService<ILicenseState>().GetModuleStatus(PaidModule.Id).ShouldBe(ModuleLicenseState.Licensed);
        app.Services.GetRequiredService<ILicenseState>().GetModuleStatus("products").ShouldBe(ModuleLicenseState.Free);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_license_uploaded_as_a_form_file_is_accepted(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = Start(database);

        using var response = await app.UploadAsFormAsync(_issuer.Sign(TestLicenseIssuer.Payload()));

        (await LicenseTestApp.ReadAsync(response)).Body.GetProperty("status").GetString().ShouldBe("valid");
        (await app.SendAsync(HttpMethod.Get, Entries)).Status.ShouldBe(200);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Rejected_files_store_nothing_and_keep_the_previous_license(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = Start(database);
        var payload = TestLicenseIssuer.Payload();
        var changed = payload with { LicenseId = "lic-forged", Stations = 50 };

        // Without a license, a rejected file stores nothing.
        ShouldBeProblem(await UploadAsync(app, _issuer.Tampered(payload, changed)), 400, "invalid_signature");
        (await app.GetLicenseAsync()).Body.GetProperty("hasLicense").GetBoolean().ShouldBeFalse();

        await ShouldUploadAsync(app, _issuer.Sign(payload));

        ShouldBeProblem(await UploadAsync(app, _issuer.Tampered(payload, changed)), 400, "invalid_signature");
        ShouldBeProblem(await UploadAsync(app, _issuer.SignWithOtherKey(changed)), 400, "invalid_signature");
        ShouldBeProblem(await UploadAsync(app, _issuer.SignWithUnknownKeyId(changed)), 400, "unknown_key");
        ShouldBeProblem(await UploadAsync(app, "not a license"), 400, "invalid_format");
        ShouldBeProblem(await UploadAsync(app, ""), 400, "invalid_format");
        ShouldBeProblem(await UploadAsync(app, new string('x', 20_000)), 400, "too_large");

        var (_, license) = await app.GetLicenseAsync();
        license.GetProperty("licenseId").GetString().ShouldBe("lic-1");
        license.GetProperty("stationLimit").GetInt32().ShouldBe(2);
        (await app.SendAsync(HttpMethod.Get, Entries)).Status.ShouldBe(200);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_ended_license_works_in_grace_then_the_module_is_read_only(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = Start(database);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today.AddDays(1))));

        // Two days after the last valid day: grace day 2 of 30, so 29 days are left counting today.
        app.Clock.Advance(TimeSpan.FromDays(3));
        var (_, license) = await app.GetLicenseAsync();
        license.GetProperty("status").GetString().ShouldBe("grace");
        license.GetProperty("graceDaysLeft").GetInt32().ShouldBe(29);
        license.GetProperty("modules")[0].GetProperty("license").GetString().ShouldBe("grace");
        (await PostEntryAsync(app)).Status.ShouldBe(201);

        app.Clock.Advance(TimeSpan.FromDays(30));
        license = (await app.GetLicenseAsync()).Body;
        license.GetProperty("status").GetString().ShouldBe("readOnly");
        license.GetProperty("graceDaysLeft").ValueKind.ShouldBe(JsonValueKind.Null);
        (await app.SendAsync(HttpMethod.Get, Entries)).Status.ShouldBe(200);
        ShouldBeProblem(await PostEntryAsync(app), 403, "license_read_only");

        // Read-only also holds incoming events, so the module creates no new data.
        await PlaceOrderAsync(app);
        await app.DispatchAsync();
        (await PaidEntriesAsync(app)).ShouldBe(["manual"]);
        (await SingleOutboxMessageAsync(app)).LastError.ShouldNotBeNull().ShouldStartWith(OutboxDispatcher.LicenseWaitPrefix);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Turning_the_clock_back_does_not_extend_the_license(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using (var app = Start(database, clock))
        {
            await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today.AddDays(1), graceDays: 5)));
            clock.Advance(TimeSpan.FromDays(10));
            (await app.GetLicenseAsync()).Body.GetProperty("status").GetString().ShouldBe("readOnly");

            clock.Advance(TimeSpan.FromDays(-10));
            var (_, license) = await app.GetLicenseAsync();
            license.GetProperty("status").GetString().ShouldBe("readOnly");
            license.GetProperty("clockBehind").GetBoolean().ShouldBeTrue();
            ShouldBeProblem(await PostEntryAsync(app), 403, "license_read_only");
        }

        // A restarted application reads the latest seen time from the database.
        await using (var app = Start(database, clock))
        {
            var (_, license) = await app.GetLicenseAsync();
            license.GetProperty("status").GetString().ShouldBe("readOnly");
            license.GetProperty("clockBehind").GetBoolean().ShouldBeTrue();

            // Removing and uploading the same license again does not reset it either.
            (await app.SendAsync(HttpMethod.Delete, LicenseEndpointExtensions.LicensePath)).Status.ShouldBe(204);
            await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today.AddDays(1), graceDays: 5)));
            (await app.GetLicenseAsync()).Body.GetProperty("status").GetString().ShouldBe("readOnly");

            // A newly issued license (new id) is the way out.
            await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(licenseId: "lic-2")));
            license = (await app.GetLicenseAsync()).Body;
            license.GetProperty("status").GetString().ShouldBe("valid");
            license.GetProperty("clockBehind").GetBoolean().ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task An_event_waits_while_unlicensed_and_is_handled_after_the_upload(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = Start(database);

        await PlaceOrderAsync(app);

        // The background service may have delivered it already (its first poll runs at startup); either way it is attempted once.
        await app.DispatchAsync();

        (await PaidEntriesAsync(app)).ShouldBeEmpty();
        (await QueryAsync<NotificationsDbContext, int>(app, db => db.Notifications.CountAsync(Ct))).ShouldBe(1);
        var waiting = await SingleOutboxMessageAsync(app);
        waiting.ProcessedAt.ShouldBeNull();
        waiting.Attempts.ShouldBe(0);
        waiting.LastError.ShouldBe(OutboxDispatcher.LicenseWaitPrefix + PaidModule.Id);
        waiting.NextAttemptAt.ShouldNotBeNull().ShouldBeGreaterThan(app.Clock.GetUtcNow().UtcDateTime);

        // Not retried in a loop while it waits.
        (await app.DispatchAsync()).ShouldBe(0);

        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload()));
        (await app.DispatchAsync()).ShouldBe(1);

        (await PaidEntriesAsync(app)).ShouldBe(["Order: 2 x Flour"]);
        (await QueryAsync<NotificationsDbContext, int>(app, db => db.Notifications.CountAsync(Ct))).ShouldBe(1);
        var processed = await SingleOutboxMessageAsync(app);
        processed.ProcessedAt.ShouldNotBeNull();
        processed.Attempts.ShouldBe(1);
        processed.LastError.ShouldBeNull();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Removing_the_license_locks_the_module_again(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = Start(database);
        await app.CreateDeviceAsync("scale-1");
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload()));
        (await app.SendAsync(HttpMethod.Get, Entries)).Status.ShouldBe(200);

        (await app.SendAsync(HttpMethod.Delete, LicenseEndpointExtensions.LicensePath)).Status.ShouldBe(204);

        ShouldBeProblem(await app.SendAsync(HttpMethod.Get, Entries), 403, "license_required");
        var (_, license) = await app.GetLicenseAsync();
        license.GetProperty("hasLicense").GetBoolean().ShouldBeFalse();
        license.GetProperty("stations").GetArrayLength().ShouldBe(0);
        app.Services.GetRequiredService<ILicenseState>().IsStationLicensed("scale-1").ShouldBeFalse();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Stations_are_chosen_on_upload_limited_and_dropped_with_their_device(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = Start(database);
        await app.CreateDeviceAsync("b-first");
        await app.CreateDeviceAsync("a-disabled", enabled: false);
        await app.CreateDeviceAsync("c-second");
        await app.CreateDeviceAsync("d-third");

        ShouldBeProblem(await app.SendAsync(HttpMethod.Put, LicenseEndpointExtensions.StationsPath, """{ "deviceIds": ["b-first"] }"""), 400, "no_license");

        // The first enabled devices by creation order.
        var (_, license) = await UploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(stations: 2)));
        Strings(license.GetProperty("stations")).ShouldBe(["b-first", "c-second"]);

        var (status, changed) = await app.SendAsync(HttpMethod.Put, LicenseEndpointExtensions.StationsPath, """{ "deviceIds": ["d-third", "a-disabled", "d-third"] }""");
        status.ShouldBe(200);
        Strings(changed.GetProperty("stations")).ShouldBe(["d-third", "a-disabled"]);
        ShouldBeProblem(await app.SendAsync(HttpMethod.Put, LicenseEndpointExtensions.StationsPath, """{ "deviceIds": ["b-first", "c-second", "d-third"] }"""), 400, "station_limit_exceeded");
        ShouldBeProblem(await app.SendAsync(HttpMethod.Put, LicenseEndpointExtensions.StationsPath, """{ "deviceIds": ["missing"] }"""), 400, "unknown_device");
        Strings((await app.GetLicenseAsync()).Body.GetProperty("stations")).ShouldBe(["d-third", "a-disabled"]);

        // The module asks through ILicenseState.
        (await app.SendAsync(HttpMethod.Get, "/api/samples-paid/stations/d-third")).Body.GetProperty("licensed").GetBoolean().ShouldBeTrue();
        (await app.SendAsync(HttpMethod.Get, "/api/samples-paid/stations/b-first")).Body.GetProperty("licensed").GetBoolean().ShouldBeFalse();

        // A new license keeps the choice that still fits.
        license = (await UploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(licenseId: "lic-2", stations: 1)))).Body;
        Strings(license.GetProperty("stations")).ShouldBe(["d-third"]);

        // Deleting a device drops it from the choice.
        (await app.SendAsync(HttpMethod.Delete, DeviceEndpointExtensions.ConfigPath + "/d-third")).Status.ShouldBe(204);
        license = (await app.GetLicenseAsync()).Body;
        license.GetProperty("stations").GetArrayLength().ShouldBe(0);
        app.Services.GetRequiredService<ILicenseState>().IsStationLicensed("d-third").ShouldBeFalse();
        await app.CreateDeviceAsync("d-third");
        app.Services.GetRequiredService<ILicenseState>().IsStationLicensed("d-third").ShouldBeFalse();

        // Nothing of the old choice is left, so the next upload chooses again.
        license = (await UploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(licenseId: "lic-3", stations: 3)))).Body;
        Strings(license.GetProperty("stations")).ShouldBe(["b-first", "c-second", "d-third"]);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Ui_modules_shows_the_license_state_of_every_module(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = Start(database);

        var (_, ui) = await app.SendAsync(HttpMethod.Get, UiEndpointExtensions.ModulesPath);
        ModuleStates(ui).ShouldBe([
            ("products", false, "free"),
            ("samples.notifications", false, "free"),
            ("samples.orders", false, "free"),
            (PaidModule.Id, true, "locked"),
            ("weighing", false, "free"),
        ]);
        ui.GetProperty("modules")[0].GetProperty("version").GetString().ShouldBe("1.0");
        Strings(ui.GetProperty("capabilities")).ShouldContain("samples.orders");
        ui.GetProperty("license").GetProperty("status").GetString().ShouldBe("none");
        ui.GetProperty("license").GetProperty("licensee").ValueKind.ShouldBe(JsonValueKind.Null);

        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today)));
        app.Clock.Advance(TimeSpan.FromDays(1));

        ui = (await app.SendAsync(HttpMethod.Get, UiEndpointExtensions.ModulesPath)).Body;
        ModuleStates(ui).ShouldContain((PaidModule.Id, true, "grace"));
        ui.GetProperty("license").GetProperty("status").GetString().ShouldBe("grace");
        ui.GetProperty("license").GetProperty("licensee").GetString().ShouldBe("Test Gıda A.Ş.");
        ui.GetProperty("license").GetProperty("graceDaysLeft").GetInt32().ShouldBe(30);

        static List<(string?, bool, string?)> ModuleStates(JsonElement ui) =>
        [
            .. ui.GetProperty("modules").EnumerateArray()
                .Select(m => (m.GetProperty("id").GetString(), m.GetProperty("requiresLicense").GetBoolean(), m.GetProperty("license").GetString())),
        ];
    }
}
