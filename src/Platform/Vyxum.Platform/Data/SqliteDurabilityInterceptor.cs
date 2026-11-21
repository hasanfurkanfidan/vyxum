using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Vyxum.Platform;

/// <summary>
/// Runs <c>PRAGMA synchronous=FULL</c> on every SQLite connection the platform opens, so that a
/// committed weighing survives a power cut. The setting is per connection and survives in the
/// connection pool, so it is applied on each open rather than once.
/// </summary>
internal sealed class SqliteDurabilityInterceptor : DbConnectionInterceptor
{
    /// <summary>The value <c>PRAGMA synchronous</c> reads back after this interceptor ran.</summary>
    public const int Full = 2;

    private const string Command = "PRAGMA synchronous=FULL;";

    private SqliteDurabilityInterceptor()
    {
    }

    /// <summary>The shared instance; it holds no state.</summary>
    public static SqliteDurabilityInterceptor Instance { get; } = new();

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Command;
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Command;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
