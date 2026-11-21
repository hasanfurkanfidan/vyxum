using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.Platform.Tests.Data;

public class PlatformMigrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(DatabaseProvider.Sqlite, "Data Source=:memory:")]
    [InlineData(DatabaseProvider.SqlServer, "Server=localhost;Database=VyxumModelCheck;Integrated Security=True;TrustServerCertificate=True")]
    public void Every_platform_model_change_has_a_migration(DatabaseProvider provider, string connectionString)
    {
        // No database is opened: the model is compared with the provider's model snapshot.
        var builder = new DbContextOptionsBuilder<PlatformDbContext>();
        new DatabaseSettings(provider, connectionString).Configure(builder);
        using var context = new PlatformDbContext(builder.Options);

        var migrations = context.GetService<IMigrationsAssembly>().Migrations;
        migrations.ShouldNotBeEmpty();
        migrations.Values.ShouldAllBe(t => t.Namespace!.EndsWith("." + provider, StringComparison.Ordinal));
        context.Database.HasPendingModelChanges().ShouldBeFalse(
            $"PlatformDbContext has model changes without a {provider} migration; see CONTRIBUTING.md");
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Platform_tables_are_migrated_with_their_own_history_table(DatabaseProvider provider)
    {
        var reason = TestDatabase.GetUnavailableReason(provider);
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        await using var database = TestDatabase.Create(provider);
        await using var host = await TestModuleHost.StartAsync(database);
        await host.Services.GetRequiredService<IModuleDatabaseInitializer>().InitializeAsync(Ct);

        await using var scope = host.CreateScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        (await platform.InstalledModules.CountAsync(Ct)).ShouldBe(0);
        platform.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single()
            .MigrationsHistoryTableName.ShouldBe("__plt_migrations");
        (await platform.Database.GetAppliedMigrationsAsync(Ct)).ShouldBe(platform.Database.GetMigrations());
        host.Services.GetRequiredService<IDatabaseBackups>().GetLatest().ShouldBeNull();
    }
}
