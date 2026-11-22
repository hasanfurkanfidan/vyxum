using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Modules.Products;
using Vyxum.Modules.Weighing;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Modules.Tests;

/// <summary>Weighing and Products with a stand-in users module: who is signed in and what they may do.</summary>
public class PermissionTests
{
    private const string Scale = "scale-1";

    private static object Body() => new { requestId = Guid.NewGuid(), deviceId = Scale };

    private static async Task<long> SaveAsync(WeighingTestApp app)
    {
        app.Scale.Set(Scale, 1.250m, unit: "kg");
        var (response, json) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", Body());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, json.ToString());
        return json.GetProperty("id").GetInt64();
    }

    private static Task<(HttpResponseMessage Response, JsonElement Body)> CancelAsync(WeighingTestApp app, long id) =>
        app.SendAsync(HttpMethod.Post, $"/api/weighing/records/{id}/cancel", new { reason = "Wrong product" });

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_record_keeps_who_weighed_it(DatabaseProvider provider)
    {
        var users = new FakeUsers(new Actor("p-7", "Ayşe Demir"), WeighingPermissions.Record);
        await using var app = await WeighingTestApp.StartAsync(provider, otherModules: [users]);

        var id = await SaveAsync(app);

        var stored = await app.WithDbAsync(db => Task.FromResult(db.Records.Single(r => r.Id == id)));
        stored.Operator.ShouldBe("Ayşe Demir");
        stored.OperatorId.ShouldBe("p-7");
        var (_, record) = await app.SendAsync(HttpMethod.Get, $"/api/weighing/records/{id}");
        record.GetProperty("operator").GetString().ShouldBe("Ayşe Demir");
    }

    [Fact]
    public async Task Without_the_permission_a_record_is_refused_with_403()
    {
        var users = new FakeUsers(new Actor("p-7", "Ayşe Demir"));
        await using var app = await WeighingTestApp.StartAsync(DatabaseProvider.Sqlite, otherModules: [users]);

        app.Scale.Set(Scale, 1.250m, unit: "kg");
        var (response, json) = await app.SendAsync(HttpMethod.Post, "/api/weighing/records", Body());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        json.GetProperty("code").GetString().ShouldBe(WeighingErrors.NotAllowed);
        (await app.WithDbAsync(db => Task.FromResult(db.Records.Count()))).ShouldBe(0);
    }

    [Fact]
    public async Task An_operator_cancels_only_their_own_record_and_only_within_the_window()
    {
        var users = new FakeUsers(new Actor("p-7", "Ayşe Demir"), WeighingPermissions.Record, WeighingPermissions.CancelOwn);
        await using var app = await WeighingTestApp.StartAsync(DatabaseProvider.Sqlite, otherModules: [users],
            settings: new Dictionary<string, string?> { ["Vyxum:Weighing:CancelOwnWindow"] = "00:10:00" });

        var mine = await SaveAsync(app);
        var late = await SaveAsync(app);
        users.Person = new Actor("p-8", "Mehmet Kaya");
        var theirs = await SaveAsync(app);
        users.Person = new Actor("p-7", "Ayşe Demir");

        (await CancelAsync(app, mine)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (other, otherJson) = await CancelAsync(app, theirs);
        other.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        otherJson.GetProperty("code").GetString().ShouldBe(WeighingErrors.NotAllowed);

        app.Clock.Advance(TimeSpan.FromMinutes(11));
        (await CancelAsync(app, late)).Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        users.Allow(WeighingPermissions.Cancel);
        (await CancelAsync(app, late)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CancelAsync(app, theirs)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Export_and_product_changes_need_their_permission()
    {
        var users = new FakeUsers(new Actor("p-7", "Ayşe Demir"));
        await using var app = await WeighingTestApp.StartAsync(DatabaseProvider.Sqlite, withProducts: true, otherModules: [users]);

        var (export, exportJson) = await app.SendAsync(HttpMethod.Get, "/api/weighing/records/export");
        export.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        exportJson.GetProperty("code").GetString().ShouldBe(WeighingErrors.NotAllowed);

        var product = new { code = "0417", name = "Pistachio", unitPrice = "780", isActive = true };
        var (refused, refusedJson) = await app.SendAsync(HttpMethod.Post, "/api/products", product);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        refusedJson.GetProperty("code").GetString().ShouldBe("products.NotAllowed");
        (await app.SendAsync(HttpMethod.Get, "/api/products")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        users.Allow(WeighingPermissions.Export, ProductsModule.EditPermission);
        using var file = await app.Client.GetAsync("/api/weighing/records/export", TestContext.Current.CancellationToken);
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await app.SendAsync(HttpMethod.Post, "/api/products", product)).Response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Without_a_users_module_records_have_no_operator_and_anyone_may_cancel()
    {
        await using var app = await WeighingTestApp.StartAsync(DatabaseProvider.Sqlite);

        var id = await SaveAsync(app);
        app.Clock.Advance(TimeSpan.FromHours(5));

        (await CancelAsync(app, id)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await app.WithDbAsync(db => Task.FromResult(db.Records.Single(r => r.Id == id)));
        stored.Operator.ShouldBeNull();
        stored.OperatorId.ShouldBeNull();
    }

    [Fact]
    public void Core_modules_declare_their_permissions_with_names_in_both_languages()
    {
        var permissions = new WeighingModule().Manifest.Permissions.Concat(new ProductsModule().Manifest.Permissions).ToList();

        permissions.Select(p => p.Id.Id).ShouldBe(
            ["weighing.record", "weighing.cancel.own", "weighing.cancel", "weighing.records.export", "products.edit"]);
        permissions.ShouldAllBe(p => p.DefaultRoles.All(r => BuiltInRoles.All.Contains(r)));
        permissions.ShouldNotContain(p => p.DefaultRoles.Contains(BuiltInRoles.Administrator));
    }

    /// <summary>A users module stand-in: one signed-in person and a set of permissions the test changes.</summary>
    private sealed class FakeUsers(Actor person, params string[] allowed) : IModule
    {
        private readonly HashSet<string> _allowed = [.. allowed];

        public Actor? Person { get; set; } = person;

        public void Allow(params string[] permissions)
        {
            lock (_allowed)
            {
                _allowed.UnionWith(permissions);
            }
        }

        public ModuleManifest Manifest { get; } = new() { Id = "fake.users", Version = new Version(1, 0), Provides = ["identity.users"] };

        public void ConfigureServices(IServiceCollection services, ModuleContext context)
        {
            services.AddSingleton<ICurrentActor>(new Current(this));
            services.AddSingleton<IAuthorization>(new Check(this));
        }

        private sealed class Current(FakeUsers users) : ICurrentActor
        {
            public Actor? Person => users.Person;
        }

        private sealed class Check(FakeUsers users) : IAuthorization
        {
            public bool IsAllowed(Permission permission)
            {
                lock (users._allowed)
                {
                    return users._allowed.Contains(permission.Id);
                }
            }
        }
    }
}
