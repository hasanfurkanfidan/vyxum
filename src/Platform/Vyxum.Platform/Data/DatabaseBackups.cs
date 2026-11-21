using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Vyxum.Platform;

/// <summary>A copy of the database taken before a schema change.</summary>
/// <param name="Path">Full path of the backup file.</param>
/// <param name="CreatedAtUtc">UTC time the backup was written.</param>
public sealed record DatabaseBackup(string Path, DateTime CreatedAtUtc);

/// <summary>Backups the platform took of the SQLite database, e.g. for an About screen.</summary>
public interface IDatabaseBackups
{
    /// <summary>Folder that holds the backups: <c>backups</c> inside the data directory.</summary>
    string Directory { get; }

    /// <summary>The newest backup, or null when there is none (always null for SQL Server).</summary>
    DatabaseBackup? GetLatest();
}

/// <summary>
/// Copies a SQLite file database with the SQLite online backup API into
/// <c>&lt;DataDirectory&gt;\backups\vyxum-&lt;yyyyMMdd-HHmmss&gt;-&lt;reason&gt;.db</c> (local time) and keeps
/// the newest <see cref="KeepCount"/>. SQL Server databases are not copied: their backup is the
/// database administrator's job, so only a warning is logged.
/// </summary>
internal sealed partial class DatabaseBackups(
    DatabaseSettings settings,
    string directory,
    TimeProvider time,
    ILogger<DatabaseBackups> logger) : IDatabaseBackups
{
    /// <summary>How many backups are kept.</summary>
    public const int KeepCount = 10;

    private const string FilePrefix = "vyxum-";
    private const string FileExtension = ".db";

    public string Directory { get; } = directory;

    public DatabaseBackup? GetLatest() =>
        List().Select(f => new DatabaseBackup(f.FullName, f.LastWriteTimeUtc)).FirstOrDefault();

    /// <summary>Backs up the database before a change named by <paramref name="reason"/>; returns null when nothing was copied.</summary>
    public async Task<DatabaseBackup?> CreateAsync(string reason, CancellationToken cancellationToken)
    {
        if (settings.Provider != DatabaseProvider.Sqlite)
        {
            LogSqlServerBackupIsExternal(logger, reason);
            return null;
        }

        var source = new SqliteConnectionStringBuilder(settings.ConnectionString);
        if (source.Mode == SqliteOpenMode.Memory || string.IsNullOrEmpty(source.DataSource) || source.DataSource == ":memory:" ||
            !File.Exists(source.DataSource))
        {
            return null;
        }

        System.IO.Directory.CreateDirectory(Directory);
        var path = NewFilePath(reason);
        var destination = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

        await using (var from = new SqliteConnection(settings.ConnectionString))
        await using (var to = new SqliteConnection(destination))
        {
            await from.OpenAsync(cancellationToken);
            await to.OpenAsync(cancellationToken);
            from.BackupDatabase(to);
        }

        var backup = new DatabaseBackup(path, File.GetLastWriteTimeUtc(path));
        LogBackupCreated(logger, path);
        DeleteOld();
        return backup;
    }

    private string NewFilePath(string reason)
    {
        var safeReason = new string(reason.Where(char.IsAsciiLetterOrDigit).ToArray());
        var stamp = time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var name = $"{FilePrefix}{stamp}-{safeReason}";
        var path = Path.Combine(Directory, name + FileExtension);
        for (var i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(Directory, $"{name}-{i}{FileExtension}");
        }

        return path;
    }

    private void DeleteOld()
    {
        foreach (var old in List().Skip(KeepCount))
        {
            try
            {
                old.Delete();
            }
            catch (IOException exception)
            {
                LogBackupNotDeleted(logger, old.FullName, exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                LogBackupNotDeleted(logger, old.FullName, exception);
            }
        }
    }

    /// <summary>Backups, newest first.</summary>
    private IEnumerable<FileInfo> List()
    {
        var folder = new DirectoryInfo(Directory);
        return folder.Exists
            ? folder.GetFiles(FilePrefix + "*" + FileExtension)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            : [];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up the database to {Path}.")]
    private static partial void LogBackupCreated(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete the old database backup {Path}.")]
    private static partial void LogBackupNotDeleted(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The SQL Server database is about to change ({Reason}) and is not backed up by Vyxum. " +
                  "Backing up SQL Server is the database administrator's responsibility.")]
    private static partial void LogSqlServerBackupIsExternal(ILogger logger, string reason);
}
