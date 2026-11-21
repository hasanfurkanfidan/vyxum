using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// Creates module tables on the shared database.
/// </summary>
/// <remarks>
/// <c>EnsureCreated</c> cannot be used per module: on a shared database it does nothing once any
/// table exists. Instead the platform keeps a <c>__vyxum_modules</c> table and, for every module
/// not recorded there, creates that module's tables with <see cref="IRelationalDatabaseCreator.CreateTablesAsync"/>.
/// Known F0 limitations: a module whose model changes after its tables were created is not upgraded
/// (only a warning is logged; per-provider EF migrations come in F2), and a crash between creating a
/// module's tables and recording it needs the database to be cleaned by hand.
/// </remarks>
internal sealed partial class ModuleDatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ModuleCatalog catalog,
    DatabaseSettings settings,
    ILogger<ModuleDatabaseInitializer> logger) : IModuleDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        settings.EnsureStorageDirectory();

        await using var scope = scopeFactory.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var creator = platform.GetService<IRelationalDatabaseCreator>();

        if (!await creator.ExistsAsync(cancellationToken))
        {
            await creator.CreateAsync(cancellationToken);
            LogDatabaseCreated(logger, settings.Provider);
        }

        if (!await PlatformTablesExistAsync(platform, cancellationToken))
        {
            await creator.CreateTablesAsync(cancellationToken);
        }

        var installed = await platform.InstalledModules.ToDictionaryAsync(m => m.ModuleId, StringComparer.Ordinal, cancellationToken);
        var contexts = catalog.Modules
            .Where(m => m.DbContextType is not null)
            .Select(m => (Module: m, Context: (ModuleDbContext)scope.ServiceProvider.GetRequiredService(m.DbContextType!)))
            .ToList();
        CheckTablePrefixesAreUnique(contexts);

        foreach (var (module, context) in contexts)
        {
            var version = module.Manifest.Version.ToString();
            if (installed.TryGetValue(module.Manifest.Id, out var existing))
            {
                if (existing.Version != version)
                {
                    LogSchemaUpgradeNotSupported(logger, module.Manifest.Id, existing.Version, version);
                }

                continue;
            }

            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
            platform.InstalledModules.Add(new InstalledModule
            {
                ModuleId = module.Manifest.Id,
                Version = version,
                InstalledAt = DateTime.UtcNow,
            });
            await platform.SaveChangesAsync(cancellationToken);
            LogModuleTablesCreated(logger, module.Manifest.Id, version);
        }
    }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the {Provider} database.")]
    private static partial void LogDatabaseCreated(ILogger logger, DatabaseProvider provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created tables of module {ModuleId} {Version}.")]
    private static partial void LogModuleTablesCreated(ILogger logger, string moduleId, string version);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Module {ModuleId} tables were created by version {InstalledVersion}; now running {Version}. " +
                  "Schema upgrades are not supported before per-provider migrations (F2).")]
    private static partial void LogSchemaUpgradeNotSupported(ILogger logger, string moduleId, string installedVersion, string version);
}
