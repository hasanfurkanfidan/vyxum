using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Vyxum.Platform.Testing;

/// <summary>
/// A throw-away database for one test: a SQLite file in a temporary directory, or a uniquely named
/// SQL Server database on the server given by <see cref="SqlServerEnvironmentVariable"/>.
/// Disposing deletes it.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    /// <summary>Environment variable with a SQL Server connection string without a database name.</summary>
    public const string SqlServerEnvironmentVariable = "VYXUM_TEST_SQLSERVER";

    private readonly string? _directory;

    private TestDatabase(DatabaseProvider provider, string connectionString, string? directory)
    {
        Provider = provider;
        ConnectionString = connectionString;
        _directory = directory;
    }

    /// <summary>The provider.</summary>
    public DatabaseProvider Provider { get; }

    /// <summary>Connection string of this test's database.</summary>
    public string ConnectionString { get; }

    /// <summary>Configuration entries that point the platform at this database.</summary>
    public IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        [DatabaseSettings.ProviderKey] = Provider.ToString(),
        [DatabaseSettings.ConnectionStringKey] = ConnectionString,
    };

    /// <summary>Why <paramref name="provider"/> cannot be used in this environment, or null when it can.</summary>
    public static string? GetUnavailableReason(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerEnvironmentVariable))
            ? $"SQL Server tests need the {SqlServerEnvironmentVariable} environment variable " +
              "(a connection string without a database name, e.g. 'Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True')."
            : null;

    /// <summary>Creates a new, empty database description. The platform's initializer creates the database itself.</summary>
    /// <exception cref="InvalidOperationException">The provider is not available; see <see cref="GetUnavailableReason"/>.</exception>
    public static TestDatabase Create(DatabaseProvider provider)
    {
        var reason = GetUnavailableReason(provider);
        if (reason is not null)
        {
            throw new InvalidOperationException(reason);
        }

        var name = $"vyxum_test_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..48];
        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                var directory = Path.Combine(Path.GetTempPath(), "vyxum-tests", name);
                Directory.CreateDirectory(directory);
                var sqlite = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "vyxum.db") };
                return new TestDatabase(provider, sqlite.ToString(), directory);

            case DatabaseProvider.SqlServer:
                var sqlServer = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(SqlServerEnvironmentVariable))
                {
                    InitialCatalog = name,
                };
                return new TestDatabase(provider, sqlServer.ToString(), null);

            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }

    /// <summary>Drops the database (SQL Server) or deletes its directory (SQLite).</summary>
    public async ValueTask DisposeAsync()
    {
        if (Provider == DatabaseProvider.SqlServer)
        {
            var options = new DbContextOptionsBuilder();
            new DatabaseSettings(Provider, ConnectionString).Configure(options);
            await using var context = new DbContext(options.Options);
            await context.Database.EnsureDeletedAsync();
            return;
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory!, recursive: true);
        }
        catch (IOException)
        {
            // A leftover file in the temp directory is harmless.
        }
    }
}
