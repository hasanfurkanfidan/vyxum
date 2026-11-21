using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Vyxum.Platform;

/// <summary>
/// Maps the setup wizard API (decision V-38) under <c>/api/setup</c>. JSON is camelCase with enums as strings.
/// </summary>
/// <remarks>
/// <para><c>GET /api/setup/status</c> is open to every caller; <c>dataDirectory</c> and <c>lastBackup.path</c> are
/// null unless the request comes from this computer.</para>
/// <para>Every <c>POST</c> is accepted only from this computer (otherwise 403, problem <c>code</c>
/// <c>setup.LocalOnly</c>) and only until the setup is completed (then 409, <c>code</c>
/// <c>setup.AlreadyCompleted</c>); <c>POST /api/setup/complete</c> stays idempotent. Invalid input is a 400
/// validation problem whose messages are UI string keys (<c>setup.*</c>).</para>
/// </remarks>
public static partial class SetupEndpointExtensions
{
    /// <summary>Path of the status endpoint.</summary>
    public const string StatusPath = "/api/setup/status";

    /// <summary>Path of the database connection test.</summary>
    public const string DatabaseTestPath = "/api/setup/database/test";

    /// <summary>Path of the database selection.</summary>
    public const string DatabasePath = "/api/setup/database";

    /// <summary>Path of the company step.</summary>
    public const string CompanyPath = "/api/setup/company";

    /// <summary>Path that marks the setup as completed.</summary>
    public const string CompletePath = "/api/setup/complete";

    /// <summary>Maximum length of the company name.</summary>
    public const int CompanyNameMaxLength = 200;

    private const int ConnectionStringMaxLength = 2000;

    /// <summary>Maps the setup endpoints.</summary>
    public static IEndpointRouteBuilder MapVyxumSetup(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(StatusPath, GetStatusAsync);
        endpoints.MapPost(DatabaseTestPath, TestDatabaseAsync);
        endpoints.MapPost(DatabasePath, SelectDatabaseAsync);
        endpoints.MapPost(CompanyPath, SaveCompanyAsync);
        endpoints.MapPost(CompletePath, CompleteAsync);
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        HttpContext context, IPlatformSettings settings, DatabaseSettings database, IDatabaseBackups backups, SetupState state)
    {
        var ct = context.RequestAborted;
        var local = AgentConnectionGate.IsLocal(context.Connection.RemoteIpAddress);
        var backup = backups.GetLatest();
        var status = new SetupStatus(
            Completed: await settings.GetAsync(PlatformSettingKeys.SetupCompletedAt, ct) is not null,
            Version: state.Version,
            Database: new SetupDatabase(database.Provider, database == DatabaseSettings.DefaultSqlite(state.DataDirectory)),
            Company: new SetupCompany(await settings.GetAsync(PlatformSettingKeys.CompanyName, ct)),
            DataDirectory: local ? state.DataDirectory : null,
            LastBackup: backup is null ? null : new SetupBackup(local ? backup.Path : null, backup.CreatedAtUtc),
            RestartPending: state.PendingDatabase is not null);
        return Json(status);
    }

    private static async Task<IResult> TestDatabaseAsync(
        HttpContext context, DatabaseSelectionInput input, IPlatformSettings settings, SetupState state, ILoggerFactory loggers)
    {
        if (await GuardAsync(context, settings, allowWhenCompleted: false) is { } rejected)
        {
            return rejected;
        }

        if (Validate(input, state, out var desired) is { } invalid)
        {
            return invalid;
        }

        if (desired.Provider == DatabaseProvider.Sqlite)
        {
            Directory.CreateDirectory(state.DataDirectory);
            return Json(new DatabaseTestResult(true, SetupStrings.Code("DbConnectionOk"), File.Exists(SqliteFile(state))));
        }

        return Json(await DatabaseConnectionTester.TestSqlServerAsync(desired.ConnectionString, Logger(loggers), context.RequestAborted));
    }

    private static async Task<IResult> SelectDatabaseAsync(
        HttpContext context,
        DatabaseSelectionInput input,
        IPlatformSettings settings,
        SetupState state,
        DatabaseSettings running,
        VyxumSettingsFile file,
        IHostRestarter restarter,
        ILoggerFactory loggers)
    {
        if (await GuardAsync(context, settings, allowWhenCompleted: false) is { } rejected)
        {
            return rejected;
        }

        if (Validate(input, state, out var desired) is { } invalid)
        {
            return invalid;
        }

        var logger = Logger(loggers);
        if (desired.Provider == DatabaseProvider.SqlServer)
        {
            var test = await DatabaseConnectionTester.TestSqlServerAsync(desired.ConnectionString, logger, context.RequestAborted);
            if (!test.Ok)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["connectionString"] = [test.Code!] });
            }
        }

        var pending = state.PendingDatabase;
        if (desired == running && pending is null)
        {
            return Json(new DatabaseSelectionResult(RestartRequired: false));
        }

        if (desired == pending)
        {
            // Already written; the restart has not happened yet.
            context.Response.OnCompleted(() => RestartAsync(restarter));
            return Json(new DatabaseSelectionResult(RestartRequired: true));
        }

        var isDefault = desired == DatabaseSettings.DefaultSqlite(state.DataDirectory);
        try
        {
            file.Update(new Dictionary<string, string?>
            {
                [DatabaseSettings.ProviderKey] = isDefault ? null : desired.Provider.ToString(),
                [DatabaseSettings.ConnectionStringKey] = isDefault ? null : desired.ConnectionString,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogSettingsNotSaved(logger, file.Path, exception);
            return Problem(StatusCodes.Status500InternalServerError, "The settings file could not be written.", "SettingsNotSaved");
        }

        LogDatabaseSelected(logger, desired.Provider, file.Path);

        if (desired == running)
        {
            // Back to what is running: the earlier, not yet applied change was undone.
            state.PendingDatabase = null;
            return Json(new DatabaseSelectionResult(RestartRequired: false));
        }

        state.PendingDatabase = desired;
        context.Response.OnCompleted(() => RestartAsync(restarter));
        return Json(new DatabaseSelectionResult(RestartRequired: true));
    }

    private static async Task<IResult> SaveCompanyAsync(HttpContext context, CompanyInput input, IPlatformSettings settings)
    {
        if (await GuardAsync(context, settings, allowWhenCompleted: false) is { } rejected)
        {
            return rejected;
        }

        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [SetupStrings.Code("CompanyNameRequired")] });
        }

        if (name.Length > CompanyNameMaxLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [SetupStrings.Code("CompanyNameTooLong")] });
        }

        await settings.SetAsync(PlatformSettingKeys.CompanyName, name, context.RequestAborted);
        return Json(new SetupCompany(name));
    }

    private static async Task<IResult> CompleteAsync(HttpContext context, IPlatformSettings settings, TimeProvider time)
    {
        if (await GuardAsync(context, settings, allowWhenCompleted: true) is { } rejected)
        {
            return rejected;
        }

        var ct = context.RequestAborted;
        var completedAt = await settings.GetAsync(PlatformSettingKeys.SetupCompletedAt, ct);
        if (completedAt is null)
        {
            completedAt = time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            await settings.SetAsync(PlatformSettingKeys.SetupCompletedAt, completedAt, ct);
        }

        return Json(new SetupCompleted(true, DateTime.Parse(completedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
    }

    /// <summary>Rejects remote callers (403) and, unless allowed, calls after completion (409).</summary>
    private static async Task<IResult?> GuardAsync(HttpContext context, IPlatformSettings settings, bool allowWhenCompleted)
    {
        if (!AgentConnectionGate.IsLocal(context.Connection.RemoteIpAddress))
        {
            return Problem(StatusCodes.Status403Forbidden, "Setup is only allowed from this computer.", "LocalOnly");
        }

        if (!allowWhenCompleted && await settings.GetAsync(PlatformSettingKeys.SetupCompletedAt, context.RequestAborted) is not null)
        {
            return Problem(StatusCodes.Status409Conflict, "Setup is already completed.", "AlreadyCompleted");
        }

        return null;
    }

    /// <summary>
    /// Checks the input and builds the database it names. SQLite always means the default file in the data
    /// directory; a connection string sent with it is ignored.
    /// </summary>
    private static IResult? Validate(DatabaseSelectionInput input, SetupState state, out DatabaseSettings desired)
    {
        desired = DatabaseSettings.DefaultSqlite(state.DataDirectory);
        if (!Enum.TryParse<DatabaseProvider>(input.Provider?.Trim(), ignoreCase: true, out var provider) ||
            !Enum.IsDefined(provider) || int.TryParse(input.Provider, out _))
        {
            return Invalid("provider", "ProviderInvalid");
        }

        if (provider == DatabaseProvider.Sqlite)
        {
            return null;
        }

        var connectionString = input.ConnectionString?.Trim();
        if (string.IsNullOrEmpty(connectionString))
        {
            return Invalid("connectionString", "ConnectionStringRequired");
        }

        if (connectionString.Length > ConnectionStringMaxLength)
        {
            return Invalid("connectionString", "ConnectionStringInvalid");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return Invalid("connectionString", "ConnectionStringInvalid");
        }

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            return Invalid("connectionString", "DatabaseNameRequired");
        }

        desired = new DatabaseSettings(DatabaseProvider.SqlServer, connectionString);
        return null;
    }

    private static Task RestartAsync(IHostRestarter restarter)
    {
        restarter.Restart();
        return Task.CompletedTask;
    }

    private static string SqliteFile(SetupState state) => Path.Combine(state.DataDirectory, DatabaseSettings.DefaultSqliteFileName);

    private static ILogger Logger(ILoggerFactory loggers) => loggers.CreateLogger("Vyxum.Platform.Setup");

    private static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [SetupStrings.Code(key)] });

    private static IResult Problem(int status, string title, string key) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = SetupStrings.Code(key) });

    private static IResult Json<T>(T value) => Results.Json(value, DeviceEndpointExtensions.JsonOptions);

    [LoggerMessage(Level = LogLevel.Information, Message = "Setup selected the {Provider} database and wrote it to {Path}.")]
    private static partial void LogDatabaseSelected(ILogger logger, DatabaseProvider provider, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Setup could not write the settings file {Path}.")]
    private static partial void LogSettingsNotSaved(ILogger logger, string path, Exception exception);

    internal sealed record DatabaseSelectionInput(string? Provider, string? ConnectionString);

    internal sealed record CompanyInput(string? Name);

    internal sealed record DatabaseSelectionResult(bool RestartRequired);

    internal sealed record SetupCompleted(bool Completed, DateTime CompletedAt);

    internal sealed record SetupDatabase(DatabaseProvider Provider, bool IsDefault);

    internal sealed record SetupCompany(string? Name);

    internal sealed record SetupBackup(string? Path, DateTime At);

    internal sealed record SetupStatus(
        bool Completed,
        string Version,
        SetupDatabase Database,
        SetupCompany Company,
        string? DataDirectory,
        SetupBackup? LastBackup,
        bool RestartPending);
}
