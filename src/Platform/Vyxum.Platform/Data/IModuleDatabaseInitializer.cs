namespace Vyxum.Platform;

/// <summary>Creates the database and the tables of every loaded module that does not have them yet.</summary>
public interface IModuleDatabaseInitializer
{
    /// <summary>Creates what is missing. Safe to call on every startup.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
