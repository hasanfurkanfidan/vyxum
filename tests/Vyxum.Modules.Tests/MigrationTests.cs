using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Modules.Products;
using Vyxum.Modules.Weighing;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.Modules.Tests;

public class MigrationTests
{
    private static readonly Guid FlourId = Guid.Parse("8a1f5c1e-7f3b-4c55-9d5e-000000000001");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TestDatabase CreateOrSkip(DatabaseProvider provider)
    {
        var reason = TestDatabase.GetUnavailableReason(provider);
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        return TestDatabase.Create(provider);
    }

    private static Task<TestModuleHost> StartAsync(TestDatabase database) =>
        TestModuleHost.StartAsync(database, new ProductsModule(), new WeighingModule());

    public static TheoryData<DatabaseProvider, Type> ContextsAndProviders()
    {
        var data = new TheoryData<DatabaseProvider, Type>();
        foreach (var provider in new[] { DatabaseProvider.Sqlite, DatabaseProvider.SqlServer })
        {
            data.Add(provider, typeof(ProductsDbContext));
            data.Add(provider, typeof(WeighingDbContext));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContextsAndProviders))]
    public void Every_model_change_has_a_migration(DatabaseProvider provider, Type contextType)
    {
        // No database is opened: the model is compared with the provider's model snapshot.
        using var context = CreateContext(contextType, provider, provider == DatabaseProvider.Sqlite
            ? "Data Source=:memory:"
            : "Server=localhost;Database=VyxumModelCheck;Integrated Security=True;TrustServerCertificate=True");

        var migrations = context.GetService<IMigrationsAssembly>().Migrations;
        migrations.ShouldNotBeEmpty();
        migrations.Values.ShouldAllBe(t => t.Namespace!.EndsWith("." + provider, StringComparison.Ordinal));
        context.Database.HasPendingModelChanges().ShouldBeFalse(
            $"{contextType.Name} has model changes without a {provider} migration; see CONTRIBUTING.md");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_new_database_gets_every_table_and_a_history_per_module(DatabaseProvider provider)
    {
        await using var database = CreateOrSkip(provider);
        await using var host = await StartAsync(database);

        await using var scope = host.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductsDbContext>();
        var weighing = scope.ServiceProvider.GetRequiredService<WeighingDbContext>();
        (await products.Products.CountAsync(Ct)).ShouldBe(0);
        (await weighing.Products.CountAsync(Ct)).ShouldBe(0);
        (await weighing.Records.CountAsync(Ct)).ShouldBe(0);
        (await products.OutboxMessages.CountAsync(Ct)).ShouldBe(0);
        (await weighing.InboxMessages.CountAsync(Ct)).ShouldBe(0);

        products.MigrationsHistoryTable.ShouldBe("__prd_migrations");
        weighing.MigrationsHistoryTable.ShouldBe("__wgh_migrations");
        await AssertFullyMigratedAsync(products);
        await AssertFullyMigratedAsync(weighing);

        // A brand-new database is not backed up.
        host.Services.GetRequiredService<IDatabaseBackups>().GetLatest().ShouldBeNull();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task A_database_created_before_migrations_upgrades_and_keeps_its_rows(DatabaseProvider provider)
    {
        await using var database = CreateOrSkip(provider);
        await PreMigrationDatabase.CreateAsync(database, Ct);

        await using var host = await StartAsync(database);

        await using (var scope = host.CreateScope())
        {
            var products = scope.ServiceProvider.GetRequiredService<ProductsDbContext>();
            var product = await products.Products.SingleAsync(Ct);
            product.Id.ShouldBe(FlourId);
            product.Name.ShouldBe("Flour");
            product.UnitPrice.ShouldBe(12.5m);

            var weighing = scope.ServiceProvider.GetRequiredService<WeighingDbContext>();
            var record = await weighing.Records.SingleAsync(Ct);
            record.Net.ShouldBe(1.234m);
            record.ProductCode.ShouldBe("100");

            await AssertFullyMigratedAsync(products);
            await AssertFullyMigratedAsync(weighing);

            // The tables are still usable after the upgrade.
            weighing.Records.Add(Record(Guid.NewGuid()));
            await weighing.SaveChangesAsync(Ct);
            (await weighing.Records.CountAsync(Ct)).ShouldBe(2);
        }

        var backup = host.Services.GetRequiredService<IDatabaseBackups>().GetLatest();
        if (provider == DatabaseProvider.Sqlite)
        {
            backup.ShouldNotBeNull();
            Path.GetFileName(backup.Path).ShouldEndWith("-migration.db");
        }
        else
        {
            backup.ShouldBeNull();
        }
    }

    [Fact]
    public async Task An_existing_sqlite_database_is_backed_up_before_migrating_and_ten_backups_are_kept()
    {
        await using var database = CreateOrSkip(DatabaseProvider.Sqlite);
        await PreMigrationDatabase.CreateAsync(database, Ct);

        var backups = Path.Combine(database.DataDirectory!, "backups");
        Directory.CreateDirectory(backups);
        for (var i = 0; i < 12; i++)
        {
            var old = Path.Combine(backups, $"vyxum-2020010{i % 9 + 1}-1200{i:00}-migration.db");
            await File.WriteAllTextAsync(old, "old", Ct);
            File.SetLastWriteTimeUtc(old, new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddHours(i));
        }

        await using var host = await StartAsync(database);

        var files = Directory.GetFiles(backups);
        files.Length.ShouldBe(10);
        var latest = host.Services.GetRequiredService<IDatabaseBackups>().GetLatest();
        latest.ShouldNotBeNull();
        latest.CreatedAtUtc.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        files.ShouldContain(latest.Path);

        // The backup is a working copy of the database as it was before the upgrade.
        var options = new DbContextOptionsBuilder<ProductsDbContext>().UseSqlite($"Data Source={latest.Path};Pooling=False").Options;
        await using var copy = new ProductsDbContext(options);
        (await copy.Products.SingleAsync(Ct)).Name.ShouldBe("Flour");
        (await copy.Database.GetAppliedMigrationsAsync(Ct)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Running_the_initializer_again_changes_nothing(DatabaseProvider provider)
    {
        await using var database = CreateOrSkip(provider);
        await PreMigrationDatabase.CreateAsync(database, Ct);

        await using (var first = await StartAsync(database))
        {
            await first.Services.GetRequiredService<IModuleDatabaseInitializer>().InitializeAsync(Ct);
        }

        var backupsAfterFirst = BackupCount(database);
        await using var second = await StartAsync(database);
        await second.Services.GetRequiredService<IModuleDatabaseInitializer>().InitializeAsync(Ct);

        BackupCount(database).ShouldBe(backupsAfterFirst);
        await using var scope = second.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductsDbContext>();
        (await products.Products.CountAsync(Ct)).ShouldBe(1);
        await AssertFullyMigratedAsync(products);
        await AssertFullyMigratedAsync(scope.ServiceProvider.GetRequiredService<WeighingDbContext>());
    }

    private static int BackupCount(TestDatabase database)
    {
        var folder = database.DataDirectory is null ? null : Path.Combine(database.DataDirectory, "backups");
        return folder is not null && Directory.Exists(folder) ? Directory.GetFiles(folder).Length : 0;
    }

    private static async Task AssertFullyMigratedAsync(DbContext context)
    {
        var all = context.Database.GetMigrations().ToList();
        all.ShouldNotBeEmpty();
        (await context.Database.GetAppliedMigrationsAsync(Ct)).ShouldBe(all);
        (await context.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
    }

    private static DbContext CreateContext(Type contextType, DatabaseProvider provider, string connectionString)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        new DatabaseSettings(provider, connectionString).Configure(builder);
        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }

    private static WeighingRecord Record(Guid requestId) => new()
    {
        RequestId = requestId,
        CreatedAtUtc = DateTime.UtcNow,
        DeviceId = "scale-1",
        ProductId = FlourId,
        ProductCode = "100",
        ProductName = "Flour",
        Gross = 1.234m,
        GrossScale = 3,
        Net = 1.234m,
        NetUnits = 1234,
        NetMilligrams = 1_234_000,
        Unit = "kg",
        Currency = "TRY",
        Status = WeighingStatus.Saved,
    };

    /// <summary>
    /// Builds a database the way the platform did before migrations: the database, the
    /// <c>__vyxum_modules</c> table, each module's tables with <c>CreateTables</c> and a row per module.
    /// </summary>
    private static class PreMigrationDatabase
    {
        public static async Task CreateAsync(TestDatabase database, CancellationToken cancellationToken)
        {
            var settings = new DatabaseSettings(database.Provider, database.ConnectionString);

            await using (var platform = new LegacyPlatformDbContext(Options<LegacyPlatformDbContext>(settings)))
            {
                var creator = platform.GetService<IRelationalDatabaseCreator>();
                if (!await creator.ExistsAsync(cancellationToken))
                {
                    await creator.CreateAsync(cancellationToken);
                }

                await creator.CreateTablesAsync(cancellationToken);
                platform.Modules.Add(new LegacyInstalledModule { ModuleId = "products", Version = "1.0", InstalledAt = DateTime.UtcNow });
                platform.Modules.Add(new LegacyInstalledModule { ModuleId = "weighing", Version = "1.0", InstalledAt = DateTime.UtcNow });
                await platform.SaveChangesAsync(cancellationToken);
            }

            await using (var products = new ProductsDbContext(Options<ProductsDbContext>(settings)))
            {
                await products.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
                products.Products.Add(new Product
                {
                    Id = FlourId,
                    Code = "100",
                    Name = "Flour",
                    UnitPrice = 12.5m,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                });
                await products.SaveChangesAsync(cancellationToken);
            }

            await using (var weighing = new WeighingDbContext(Options<WeighingDbContext>(settings)))
            {
                await weighing.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
                weighing.Records.Add(Record(Guid.NewGuid()));
                await weighing.SaveChangesAsync(cancellationToken);
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }

        private static DbContextOptions<T> Options<T>(DatabaseSettings settings)
            where T : DbContext
        {
            var builder = new DbContextOptionsBuilder<T>();
            settings.Configure(builder);
            return builder.Options;
        }
    }

    /// <summary>The platform's table as it was before migrations.</summary>
    private sealed class LegacyPlatformDbContext(DbContextOptions<LegacyPlatformDbContext> options) : DbContext(options)
    {
        public DbSet<LegacyInstalledModule> Modules => Set<LegacyInstalledModule>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<LegacyInstalledModule>(entity =>
            {
                entity.ToTable("__vyxum_modules");
                entity.HasKey(m => m.ModuleId);
                entity.Property(m => m.ModuleId).HasMaxLength(200);
                entity.Property(m => m.Version).HasMaxLength(50);
            });
    }

    private sealed class LegacyInstalledModule
    {
        public required string ModuleId { get; init; }

        public required string Version { get; set; }

        public DateTime InstalledAt { get; init; }
    }
}
