using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// Brings the shared database up to date with EF Core migrations: first the platform's, then every
/// module's, each with its own history table.
/// </summary>
/// <remarks>
/// <para>
/// Baseline: databases created before migrations (tables made with <c>CreateTables</c> and recorded
/// in <c>__vyxum_modules</c>) have the tables of the first migration but no history rows. For the
/// platform, and for every module recorded as installed, an empty history therefore means the first
/// migration is marked as applied, and only the later ones run.
/// </para>
/// <para>
/// Before anything changes in an existing database, <see cref="DatabaseBackups"/> copies it (SQLite).
/// A module that ships no migrations for the active provider (e.g. a test module) gets its tables
/// created once, as before; such a module cannot be upgraded until it ships migrations.
/// </para>
/// </remarks>
internal sealed partial class ModuleDatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ModuleCatalog catalog,
    DatabaseSettings settings,
    DatabaseBackups backups,
    ILogger<ModuleDatabaseInitializer> logger) : IModuleDatabaseInitializer
{
    private const string PlatformName = "platform";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        settings.EnsureStorageDirectory();

        await using var scope = scopeFactory.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var contexts = catalog.Modules
            .Where(m => m.DbContextType is not null)
            .Select(m => (Module: m, Context: (ModuleDbContext)scope.ServiceProvider.GetRequiredService(m.DbContextType!)))
            .ToList();
        CheckTablePrefixesAreUnique(contexts);

        var databaseExisted = await platform.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken);
        var populated = databaseExisted && await PlatformTablesExistAsync(platform, cancellationToken);
        var installed = populated
            ? await platform.InstalledModules.ToDictionaryAsync(m => m.ModuleId, StringComparer.Ordinal, cancellationToken)
            : new Dictionary<string, InstalledModule>(StringComparer.Ordinal);

        var platformPlan = await PlanAsync(platform, isInstalled: populated, populated, cancellationToken);
        var modulePlans = new List<(ModuleDescriptor Module, ModuleDbContext Context, MigrationPlan Plan)>();
        foreach (var (module, context) in contexts)
        {
            var plan = await PlanAsync(context, installed.ContainsKey(module.Manifest.Id), populated, cancellationToken);
            modulePlans.Add((module, context, plan));
        }

        if (populated && (platformPlan.HasWork || modulePlans.Any(p => p.Plan.HasWork)))
        {
            await backups.CreateAsync("migration", cancellationToken);
        }

        await ApplyAsync(platform, PlatformName, platformPlan, cancellationToken);
        if (!databaseExisted)
        {
            LogDatabaseCreated(logger, settings.Provider);
        }

        foreach (var (module, context, plan) in modulePlans)
        {
            var id = module.Manifest.Id;
            if (plan.HasMigrations)
            {
                await ApplyAsync(context, id, plan, cancellationToken);
            }
            else if (plan.HasWork)
            {
                await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
                LogModuleWithoutMigrations(logger, id, ProviderFolder(context));
            }

            await RecordAsync(platform, installed, module, cancellationToken);
        }
    }

    /// <summary>Works out what <see cref="ApplyAsync"/> has to do for one context, without changing anything.</summary>
    private static async Task<MigrationPlan> PlanAsync(DbContext context, bool isInstalled, bool populated, CancellationToken cancellationToken)
    {
        var all = context.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();
        var applied = populated
            ? (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal)
            : [];

        if (all.Count == 0)
        {
            return new MigrationPlan(HasMigrations: false, Baseline: null, Pending: isInstalled ? 0 : 1);
        }

        var baseline = isInstalled && applied.Count == 0 ? all[0] : null;
        var pending = all.Count(id => id != baseline && !applied.Contains(id));
        return new MigrationPlan(HasMigrations: true, baseline, pending);
    }

    private async Task ApplyAsync(DbContext context, string name, MigrationPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Baseline is not null)
        {
            // Rows are written through EF's history repository, so no provider-specific SQL is needed here.
            var history = context.GetService<IHistoryRepository>();
            await history.CreateIfNotExistsAsync(cancellationToken);
            var insert = history.GetInsertScript(new HistoryRow(plan.Baseline, ProductInfo.GetVersion()));
            await context.Database.ExecuteSqlRawAsync(insert, cancellationToken);
            LogBaselineRecorded(logger, name, plan.Baseline);
        }

        if (plan.Pending > 0)
        {
            await context.Database.MigrateAsync(cancellationToken);
            LogMigrationsApplied(logger, name, plan.Pending);
        }
    }

    private static async Task RecordAsync(
        PlatformDbContext platform,
        Dictionary<string, InstalledModule> installed,
        ModuleDescriptor module,
        CancellationToken cancellationToken)
    {
        var version = module.Manifest.Version.ToString();
        if (installed.TryGetValue(module.Manifest.Id, out var existing))
        {
            if (existing.Version == version)
            {
                return;
            }

            existing.Version = version;
        }
        else
        {
            platform.InstalledModules.Add(new InstalledModule
            {
                ModuleId = module.Manifest.Id,
                Version = version,
                InstalledAt = DateTime.UtcNow,
            });
        }

        await platform.SaveChangesAsync(cancellationToken);
    }

    private static string ProviderFolder(DbContext context) =>
        context.Database.ProviderName?.Split('.')[^1] ?? string.Empty;

    /// <summary>
    /// Probes the platform table with a normal query, so no provider-specific catalog SQL is needed.
    /// A missing table makes the provider throw a <see cref="DbException"/>.
    /// </summary>
    private static async Task<bool> PlatformTablesExistAsync(PlatformDbContext platform, CancellationToken cancellationToken)
    {
        try
        {
            await platform.InstalledModules.AnyAsync(cancellationToken);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
    }

    private static void CheckTablePrefixesAreUnique(IEnumerable<(ModuleDescriptor Module, ModuleDbContext Context)> contexts)
    {
        var clashes = contexts
            .GroupBy(c => c.Context.TablePrefix, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"Table prefix '{g.Key}' is used by more than one module: " +
                         string.Join(", ", g.Select(c => $"'{c.Module.Manifest.Id}'")) + ".")
            .ToList();
        if (clashes.Count > 0)
        {
            throw new ModuleValidationException(clashes);
        }
    }

    /// <param name="HasMigrations">Whether the context ships migrations for the active provider.</param>
    /// <param name="Baseline">The first migration, to be marked as applied without running it; or null.</param>
    /// <param name="Pending">Migrations to run (for a context without migrations: 1 when its tables must be created).</param>
    private sealed record MigrationPlan(bool HasMigrations, string? Baseline, int Pending)
    {
        public bool HasWork => Baseline is not null || Pending > 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the {Provider} database.")]
    private static partial void LogDatabaseCreated(ILogger logger, DatabaseProvider provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} migration(s) of {Name}.")]
    private static partial void LogMigrationsApplied(ILogger logger, string name, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The tables of {Name} predate migrations; recorded {MigrationId} as applied.")]
    private static partial void LogBaselineRecorded(ILogger logger, string name, string migrationId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Module {ModuleId} ships no {Provider} migrations; created its tables directly. " +
                  "Its schema cannot be upgraded until it ships migrations.")]
    private static partial void LogModuleWithoutMigrations(ILogger logger, string moduleId, string provider);
}
