using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Creates a context for <c>dotnet ef</c> with the provider named after <c>--</c>:
/// <c>dotnet ef migrations add Name ... -- --provider Sqlite</c> (or <c>SqlServer</c>).
/// A module adds one line to use it:
/// <code>internal sealed class DesignTimeFactory : DesignTimeDbContextFactory&lt;MyDbContext&gt;;</code>
/// No database is opened while a migration is generated; the connection strings are placeholders.
/// </summary>
/// <typeparam name="TContext">The context; it needs a constructor that takes <see cref="DbContextOptions{TContext}"/>.</typeparam>
public abstract class DesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
    where TContext : DbContext
{
    /// <summary>The argument that names the provider.</summary>
    public const string ProviderArgument = "--provider";

    /// <inheritdoc />
    public TContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var index = Array.IndexOf(args, ProviderArgument);
        var provider = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var options = new DbContextOptionsBuilder<TContext>();
        switch (provider)
        {
            case ProviderMigrations.SqliteFolder:
                options.UseSqlite("Data Source=design-time.db");
                break;
            case ProviderMigrations.SqlServerFolder:
                options.UseSqlServer("Server=localhost;Database=VyxumDesignTime;Integrated Security=True;TrustServerCertificate=True");
                break;
            default:
                throw new InvalidOperationException(
                    $"Name the provider after '--': '-- {ProviderArgument} {ProviderMigrations.SqliteFolder}' " +
                    $"or '-- {ProviderArgument} {ProviderMigrations.SqlServerFolder}'.");
        }

        return (TContext)Activator.CreateInstance(typeof(TContext), options.Options)!;
    }
}
