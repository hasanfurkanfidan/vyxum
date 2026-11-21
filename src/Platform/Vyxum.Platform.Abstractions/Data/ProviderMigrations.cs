using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Lets one DbContext type carry generated migrations for several database providers.
/// </summary>
/// <remarks>
/// Migrations of each provider live in their own folder, and so in their own namespace, whose last
/// segment is the provider's short name: <c>Migrations/Sqlite</c> and <c>Migrations/SqlServer</c>
/// (namespaces <c>...Migrations.Sqlite</c> and <c>...Migrations.SqlServer</c>). EF Core would otherwise
/// see both sets as one; the <see cref="IMigrationsAssembly"/> installed here only returns the migrations
/// and the model snapshot of the active provider. Every context also gets its own history table, so the
/// platform and every module migrate independently on one shared database.
/// <see cref="ModuleDbContext"/> applies this automatically; module code does not call it.
/// </remarks>
public static class ProviderMigrations
{
    /// <summary>Folder (and last namespace segment) of the SQLite migrations.</summary>
    public const string SqliteFolder = "Sqlite";

    /// <summary>Folder (and last namespace segment) of the SQL Server migrations.</summary>
    public const string SqlServerFolder = "SqlServer";

    /// <summary>
    /// Sets the history table to <paramref name="historyTableName"/> and installs the provider-aware
    /// migrations lookup. Call it from <c>OnConfiguring</c>, after the provider has been configured.
    /// </summary>
    public static void Configure(DbContextOptionsBuilder optionsBuilder, string historyTableName)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyTableName);

        var relational = optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault();
        if (relational is not null && relational.MigrationsHistoryTableName != historyTableName)
        {
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
                relational.WithMigrationsHistoryTableName(historyTableName));
        }

        optionsBuilder.ReplaceService<IMigrationsAssembly, ProviderMigrationsAssembly>();
    }

    /// <summary>The folder name for a provider: the last segment of its EF Core provider name.</summary>
    internal static string FolderOf(string providerName)
    {
        var dot = providerName.LastIndexOf('.');
        return dot < 0 ? providerName : providerName[(dot + 1)..];
    }

    /// <summary>The migrations of the context's own assembly whose namespace ends with the active provider's folder.</summary>
    private sealed class ProviderMigrationsAssembly : IMigrationsAssembly
    {
        private readonly Type _contextType;
        private readonly string _folder;
        private readonly IMigrationsIdGenerator _idGenerator;
        private readonly Lazy<IReadOnlyDictionary<string, TypeInfo>> _migrations;
        private readonly Lazy<ModelSnapshot?> _modelSnapshot;

        public ProviderMigrationsAssembly(ICurrentDbContext currentContext, IDatabaseProvider provider, IMigrationsIdGenerator idGenerator)
        {
            _contextType = currentContext.Context.GetType();
            _folder = FolderOf(provider.Name);
            _idGenerator = idGenerator;
            Assembly = _contextType.Assembly;
            _migrations = new Lazy<IReadOnlyDictionary<string, TypeInfo>>(FindMigrations);
            _modelSnapshot = new Lazy<ModelSnapshot?>(FindModelSnapshot);
        }

        public IReadOnlyDictionary<string, TypeInfo> Migrations => _migrations.Value;

        public ModelSnapshot? ModelSnapshot => _modelSnapshot.Value;

        public Assembly Assembly { get; }

        public string? FindMigrationId(string nameOrId)
        {
            Func<string, bool> matches = _idGenerator.IsValidId(nameOrId)
                ? id => string.Equals(id, nameOrId, StringComparison.OrdinalIgnoreCase)
                : id => string.Equals(_idGenerator.GetName(id), nameOrId, StringComparison.OrdinalIgnoreCase);
            return Migrations.Keys.FirstOrDefault(matches);
        }

        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
        {
            ArgumentNullException.ThrowIfNull(migrationClass);
            var migration = (Migration)Activator.CreateInstance(migrationClass.AsType())!;
            migration.ActiveProvider = activeProvider;
            return migration;
        }

        private IEnumerable<TypeInfo> OwnTypes<TBase>() =>
            Assembly.DefinedTypes.Where(t =>
                t is { IsAbstract: false, IsGenericTypeDefinition: false } &&
                t.IsSubclassOf(typeof(TBase)) &&
                t.GetCustomAttribute<DbContextAttribute>()?.ContextType == _contextType &&
                FolderOf(t.Namespace ?? string.Empty) == _folder);

        private IReadOnlyDictionary<string, TypeInfo> FindMigrations()
        {
            var result = new SortedDictionary<string, TypeInfo>(StringComparer.Ordinal);
            foreach (var type in OwnTypes<Migration>())
            {
                var id = type.GetCustomAttribute<MigrationAttribute>()?.Id;
                if (id is null)
                {
                    continue;
                }

                if (!result.TryAdd(id, type))
                {
                    throw new InvalidOperationException(
                        $"Migration id '{id}' of {_contextType.Name} is used by both {result[id].FullName} and {type.FullName}.");
                }
            }

            return result;
        }

        private ModelSnapshot? FindModelSnapshot()
        {
            var snapshots = OwnTypes<ModelSnapshot>().ToList();
            return snapshots.Count switch
            {
                0 => null,
                1 => (ModelSnapshot)Activator.CreateInstance(snapshots[0].AsType())!,
                _ => throw new InvalidOperationException(
                    $"{_contextType.Name} has more than one {_folder} model snapshot: " +
                    string.Join(", ", snapshots.Select(s => s.FullName)) + "."),
            };
        }
    }
}
