using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Vyxum.Platform;

/// <summary>The outcome of a connection test. <paramref name="Code"/> is a UI string key; null when the test succeeded.</summary>
/// <param name="Ok">True when the server accepted the login.</param>
/// <param name="Code">Why the test failed, or a note on success (<c>setup.DbWillBeCreated</c>).</param>
/// <param name="DatabaseExists">Whether the database exists; null when unknown.</param>
internal sealed record DatabaseTestResult(bool Ok, string? Code, bool? DatabaseExists);

/// <summary>
/// Tries a database connection for the setup wizard. Connection strings and passwords never reach the logs:
/// only SQL Server error numbers are logged.
/// </summary>
internal static partial class DatabaseConnectionTester
{
    /// <summary>Connect timeout of a test, in seconds.</summary>
    public const int TimeoutSeconds = 5;

    // Login failures: wrong password or user, disabled login, expired or must-change password.
    private static readonly HashSet<int> LoginFailed = [18452, 18456, 18470, 18486, 18487, 18488];

    // Network and instance errors: not found, refused, timeout, named instance not reachable.
    private static readonly HashSet<int> ServerNotFound = [-2, -1, 2, 26, 40, 53, 64, 121, 258, 1225, 10053, 10054, 10060, 10061, 10065, 11001, 11002, 11004];

    private const int CannotOpenDatabase = 4060;

    public static async Task<DatabaseTestResult> TestSqlServerAsync(string connectionString, ILogger logger, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = TimeoutSeconds,
            ConnectRetryCount = 0,
            Pooling = false,
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds * 3));
        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(timeout.Token);
            return new DatabaseTestResult(true, SetupStrings.Code("DbConnectionOk"), true);
        }
        catch (SqlException exception) when (exception.Errors.Cast<SqlError>().Any(e => e.Number == CannotOpenDatabase))
        {
            // The login works but the database does not exist (or is not accessible); the host creates it on start.
            return new DatabaseTestResult(true, SetupStrings.Code("DbWillBeCreated"), false);
        }
        catch (SqlException exception)
        {
            LogTestFailed(logger, exception.Number);
            var code = LoginFailed.Contains(exception.Number) ? "DbLoginFailed"
                : ServerNotFound.Contains(exception.Number) ? "DbServerNotFound"
                : "DbUnknownError";
            return new DatabaseTestResult(false, SetupStrings.Code(code), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTestTimedOut(logger);
            return new DatabaseTestResult(false, SetupStrings.Code("DbServerNotFound"), null);
        }
        catch (InvalidOperationException exception)
        {
            LogTestError(logger, exception.GetType().Name);
            return new DatabaseTestResult(false, SetupStrings.Code("DbUnknownError"), null);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL Server connection test failed with error number {Number}.")]
    private static partial void LogTestFailed(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL Server connection test timed out.")]
    private static partial void LogTestTimedOut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL Server connection test failed: {ExceptionType}.")]
    private static partial void LogTestError(ILogger logger, string exceptionType);
}
