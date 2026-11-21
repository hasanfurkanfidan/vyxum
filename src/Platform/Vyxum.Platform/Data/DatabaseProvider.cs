namespace Vyxum.Platform;

/// <summary>Supported database providers.</summary>
public enum DatabaseProvider
{
    /// <summary>SQLite file database (default, zero configuration).</summary>
    Sqlite,

    /// <summary>Microsoft SQL Server.</summary>
    SqlServer,
}
