using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Vyxum.Samples.Paid;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>
/// Licensing edge cases on both providers: the clock rule and its writer, station rules, a failing database.
/// </summary>
public sealed class LicenseHardeningTests : IDisposable
{
    private const string Entries = "/api/samples-paid/entries";

    private readonly TestLicenseIssuer _issuer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _issuer.Dispose();

    private static async Task ShouldUploadAsync(LicenseTestApp app, string file)
    {
        using var response = await app.UploadAsync(file);
        var (status, body) = await LicenseTestApp.ReadAsync(response);
        status.ShouldBe(200, body.ToString());
    }

    private static async Task<JsonElement> LicenseAsync(LicenseTestApp app) => (await app.GetLicenseAsync()).Body;

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_short_forward_glitch_of_the_clock_is_not_kept(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using (var app = new LicenseTestApp(database, _issuer, clock, maintenance: false))
        {
            await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today.AddDays(1), graceDays: 5)));
            await app.PersistAsync();
            var stored = (await app.StoredLastSeenAsync()).ShouldNotBeNull();

            // The clock reads a year ahead: the evaluation follows it, but the time is not persisted.
            clock.Advance(TimeSpan.FromDays(365));
            var license = await LicenseAsync(app);
            license.GetProperty("status").GetString().ShouldBe("readOnly");
            license.GetProperty("clockAhead").GetBoolean().ShouldBeTrue();
            clock.Elapse(LicenseManager.ForwardJumpConfirmation - TimeSpan.FromMinutes(5));
            (await LicenseAsync(app)).GetProperty("clockAhead").GetBoolean().ShouldBeTrue();
            await app.PersistAsync();
            (await app.StoredLastSeenAsync()).ShouldBe(stored);

            // Within six hours the clock is right again: nothing of the glitch is left.
            clock.Advance(TimeSpan.FromDays(-365));
            license = await LicenseAsync(app);
            license.GetProperty("status").GetString().ShouldBe("valid");
            license.GetProperty("clockAhead").GetBoolean().ShouldBeFalse();
            license.GetProperty("clockBehind").GetBoolean().ShouldBeFalse();
            (await app.SendAsync(HttpMethod.Post, Entries, """{ "text": "after glitch" }""")).Status.ShouldBe(201);
            await app.PersistAsync();
            (await app.StoredLastSeenAsync()).ShouldNotBeNull().ShouldBeLessThan(stored.AddDays(1));
        }

        await using (var app = new LicenseTestApp(database, _issuer, clock, maintenance: false))
        {
            (await LicenseAsync(app)).GetProperty("status").GetString().ShouldBe("valid");
        }
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_jump_that_returns_before_six_hours_starts_the_confirmation_again(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using var app = new LicenseTestApp(database, _issuer, clock, maintenance: false);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload()));
        await app.PersistAsync();
        var stored = (await app.StoredLastSeenAsync()).ShouldNotBeNull();

        clock.Advance(TimeSpan.FromDays(30));
        (await LicenseAsync(app)).GetProperty("clockAhead").GetBoolean().ShouldBeTrue();
        clock.Elapse(TimeSpan.FromHours(5));
        clock.Advance(TimeSpan.FromDays(-30));
        (await LicenseAsync(app)).GetProperty("clockAhead").GetBoolean().ShouldBeFalse();

        // Ahead again: the five hours seen before do not count.
        clock.Advance(TimeSpan.FromDays(30));
        (await LicenseAsync(app)).GetProperty("clockAhead").GetBoolean().ShouldBeTrue();
        clock.Elapse(TimeSpan.FromHours(2));
        (await LicenseAsync(app)).GetProperty("clockAhead").GetBoolean().ShouldBeTrue();
        await app.PersistAsync();
        (await app.StoredLastSeenAsync()).ShouldNotBeNull().ShouldBeLessThan(stored.AddDays(2));

        clock.Elapse(TimeSpan.FromHours(4));
        (await LicenseAsync(app)).GetProperty("clockAhead").GetBoolean().ShouldBeFalse();
        await app.PersistAsync();
        (await app.StoredLastSeenAsync()).ShouldNotBeNull().ShouldBeGreaterThan(stored.AddDays(30));
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Reading_the_license_never_writes_the_latest_seen_time(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using var app = new LicenseTestApp(database, _issuer, clock, maintenance: false);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload()));
        (await app.StoredLastSeenAsync()).ShouldBeNull();
        await app.PersistAsync();
        var stored = (await app.StoredLastSeenAsync()).ShouldNotBeNull();

        // A normal advance is accepted, but only the writer stores it.
        clock.Advance(TimeSpan.FromHours(3));
        (await LicenseAsync(app)).GetProperty("clockAhead").GetBoolean().ShouldBeFalse();
        app.Services.GetRequiredService<ILicenseState>().Status.ShouldBe(LicenseCondition.Valid);
        (await app.StoredLastSeenAsync()).ShouldBe(stored);

        await app.PersistAsync();
        (await app.StoredLastSeenAsync()).ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(stored.AddHours(3));
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task The_license_state_can_be_read_inside_an_open_write_transaction(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using var app = new LicenseTestApp(database, _issuer, clock, maintenance: false);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload()));
        await app.PersistAsync();
        var stored = await app.StoredLastSeenAsync();
        clock.Advance(TimeSpan.FromHours(3));

        // A module handler's transaction that has already written (SQLite: the database write lock is held).
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaidDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            db.Entries.Add(new Entry { Id = Guid.NewGuid(), Text = "in transaction", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);

            app.Licenses.Invalidate();
            var watch = Stopwatch.StartNew();
            var state = scope.ServiceProvider.GetRequiredService<ILicenseState>();
            state.GetModuleStatus(PaidModule.Id).ShouldBe(ModuleLicenseState.Licensed);
            state.LicenseeName.ShouldBe("Test Gıda A.Ş.");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));

            await transaction.CommitAsync(Ct);
        }

        (await app.StoredLastSeenAsync()).ShouldBe(stored);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task The_maintenance_service_persists_the_latest_seen_time_after_an_upload(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = new LicenseTestApp(database, _issuer);

        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload()));

        var watch = Stopwatch.StartNew();
        while (await app.StoredLastSeenAsync() is null)
        {
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
            await Task.Delay(50, Ct);
        }
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Stations_are_licensed_only_while_the_license_is_in_force(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using var app = new LicenseTestApp(database, _issuer, clock, maintenance: false);
        await app.CreateDeviceAsync("scale-1");
        var state = app.Services.GetRequiredService<ILicenseState>();

        // Not started yet: the station is chosen but not licensed.
        var future = TestLicenseIssuer.Payload() with { ValidFrom = TestLicenseIssuer.Today.AddDays(3) };
        await ShouldUploadAsync(app, _issuer.Sign(future));
        state.Status.ShouldBe(LicenseCondition.NotYetValid);
        state.LicensedStations.ShouldBe(["scale-1"]);
        state.IsStationLicensed("scale-1").ShouldBeFalse();

        // Valid, then grace: licensed.
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(licenseId: "lic-2", validUntil: TestLicenseIssuer.Today, graceDays: 1)));
        state.IsStationLicensed("scale-1").ShouldBeTrue();
        clock.Advance(TimeSpan.FromDays(1));
        state.Status.ShouldBe(LicenseCondition.Grace);
        state.IsStationLicensed("scale-1").ShouldBeTrue();

        // Read-only: no longer licensed, although still chosen.
        clock.Advance(TimeSpan.FromDays(1));
        state.Status.ShouldBe(LicenseCondition.ReadOnly);
        state.LicensedStations.ShouldBe(["scale-1"]);
        state.IsStationLicensed("scale-1").ShouldBeFalse();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Capabilities_of_a_paid_module_count_only_while_it_may_run(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using var app = new LicenseTestApp(database, _issuer, clock, maintenance: false);
        var registry = app.Services.GetRequiredService<ICapabilityRegistry>();

        async Task<string[]> UiCapabilitiesAsync()
        {
            var (_, ui) = await app.SendAsync(HttpMethod.Get, UiEndpointExtensions.ModulesPath);
            return [.. ui.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()!)];
        }

        // Locked: not available, although the module is installed and declares it.
        registry.IsAvailable(PaidModule.EntriesCapability).ShouldBeFalse();
        registry.IsAvailable("samples.orders").ShouldBeTrue();
        (await UiCapabilitiesAsync()).ShouldNotContain(PaidModule.EntriesCapability);
        (await UiCapabilitiesAsync()).ShouldContain("samples.orders");
        var (_, modules) = await app.SendAsync(HttpMethod.Get, UiEndpointExtensions.ModulesPath);
        modules.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("id").GetString() == PaidModule.Id)
            .GetProperty("provides").EnumerateArray().Select(c => c.GetString()).ShouldBe([PaidModule.EntriesCapability]);

        // Licensed, then in grace: available.
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today, graceDays: 1)));
        registry.IsAvailable(PaidModule.EntriesCapability).ShouldBeTrue();
        (await UiCapabilitiesAsync()).ShouldContain(PaidModule.EntriesCapability);
        clock.Advance(TimeSpan.FromDays(1));
        registry.IsAvailable(PaidModule.EntriesCapability).ShouldBeTrue();

        // Read-only: not available.
        clock.Advance(TimeSpan.FromDays(1));
        app.Services.GetRequiredService<ILicenseState>().Status.ShouldBe(LicenseCondition.ReadOnly);
        registry.IsAvailable(PaidModule.EntriesCapability).ShouldBeFalse();
        (await UiCapabilitiesAsync()).ShouldNotContain(PaidModule.EntriesCapability);

        // Removed: not available.
        clock.Advance(TimeSpan.FromDays(-2));
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(licenseId: "lic-2")));
        registry.IsAvailable(PaidModule.EntriesCapability).ShouldBeTrue();
        (await app.SendAsync(HttpMethod.Delete, LicenseEndpointExtensions.LicensePath)).Status.ShouldBe(204);
        registry.IsAvailable(PaidModule.EntriesCapability).ShouldBeFalse();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_database_failure_while_reading_the_license_keeps_the_module_closed(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = new LicenseTestApp(database, _issuer, maintenance: false);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload()));
        (await app.SendAsync(HttpMethod.Get, Entries)).Status.ShouldBe(200);

        // The settings table is gone: the stored file cannot be read.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE plt_Settings", Ct);
        }

        app.Licenses.Invalidate();

        using (var get = await app.Client.GetAsync(Entries, Ct))
        {
            ((int)get.StatusCode).ShouldBe(500);
        }

        using (var post = await app.Client.PostAsync(Entries, LicenseTestApp.Json("""{ "text": "while broken" }"""), Ct))
        {
            ((int)post.StatusCode).ShouldBe(500);
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PaidDbContext>().Entries.CountAsync(Ct)).ShouldBe(0);
        }

        Should.Throw<Exception>(() => app.Services.GetRequiredService<ILicenseState>().GetModuleStatus(PaidModule.Id));
    }
}
