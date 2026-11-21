using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Vyxum.Platform;

/// <summary>The database provider and connection string all module contexts use.</summary>
/// <param name="Provider">The provider.</param>
/// <param name="ConnectionString">The connection string.</param>
public sealed record DatabaseSettings(DatabaseProvider Provider, string ConnectionString)
{
    /// <summary>Configuration key of the provider: <c>Sqlite</c> (default) or <c>SqlServer</c>.</summary>
    public const string ProviderKey = "Vyxum:Database:Provider";

    /// <summary>Configuration key of the connection string. Optional for SQLite.</summary>
    public const string ConnectionStringKey = "Vyxum:Database:ConnectionString";

    /// <summary>Configuration key of the data directory that holds the default SQLite file.</summary>
    public const string DataDirectoryKey = "Vyxum:DataDirectory";

    /// <summary>File name of the default SQLite database inside the data directory.</summary>
    public const string DefaultSqliteFileName = "vyxum.db";

    /// <summary>
    /// Reads the settings. With nothing configured the result is SQLite with
    /// <c>vyxum.db</c> in the data directory (see <see cref="GetDataDirectory"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The configuration is invalid.</exception>
    public static DatabaseSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var providerText = configuration[ProviderKey];
        var provider = DatabaseProvider.Sqlite;
        if (!string.IsNullOrWhiteSpace(providerText) &&
            (!Enum.TryParse(providerText.Trim(), ignoreCase: true, out provider) || !Enum.IsDefined(provider)))
        {
            throw new InvalidOperationException(
                $"Configuration '{ProviderKey}' has unknown value '{providerText}'. Use 'Sqlite' or 'SqlServer'.");
        }

        var connectionString = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (provider != DatabaseProvider.Sqlite)
            {
                throw new InvalidOperationException(
                    $"Configuration '{ConnectionStringKey}' is required when '{ProviderKey}' is '{provider}'.");
            }

            var path = Path.Combine(GetDataDirectory(configuration), DefaultSqliteFileName);
            connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        }

        return new DatabaseSettings(provider, connectionString);
    }

    /// <summary>
    /// The data directory: <c>Vyxum:DataDirectory</c> when configured, otherwise
    /// <c>%ProgramData%\Vyxum</c> on Windows and <c>./data</c> elsewhere. Always a full path.
    /// </summary>
    public static string GetDataDirectory(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration[DataDirectoryKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vyxum")
            : Path.GetFullPath("data");
    }

    /// <summary>
    /// Configures <paramref name="options"/> for this provider and connection string. SQLite
    /// connections run with <c>PRAGMA synchronous=FULL</c>.
    /// </summary>
    public void Configure(DbContextOptionsBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);

        switch (Provider)
        {
            case DatabaseProvider.Sqlite:
                options.UseSqlite(ConnectionString).AddInterceptors(SqliteDurabilityInterceptor.Instance);
                break;
            case DatabaseProvider.SqlServer:
                options.UseSqlServer(ConnectionString);
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{Provider}'.");
        }
    }

    /// <summary>For SQLite file databases, creates the directory that will hold the file.</summary>
    internal void EnsureStorageDirectory()
    {
        if (Provider != DatabaseProvider.Sqlite)
        {
            return;
        }

        var builder = new SqliteConnectionStringBuilder(ConnectionString);
        if (builder.Mode == SqliteOpenMode.Memory || string.IsNullOrEmpty(builder.DataSource) || builder.DataSource == ":memory:")
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>The connection string is left out so that passwords never reach logs.</summary>
    public override string ToString() => $"DatabaseSettings {{ Provider = {Provider} }}";
}
