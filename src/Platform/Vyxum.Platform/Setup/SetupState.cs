using System.Reflection;

namespace Vyxum.Platform;

/// <summary>Process-wide facts the setup API reports.</summary>
internal sealed class SetupState(string dataDirectory, string version)
{
    private readonly Lock _lock = new();
    private DatabaseSettings? _pendingDatabase;

    /// <summary>The data directory.</summary>
    public string DataDirectory { get; } = dataDirectory;

    /// <summary>The host's informational version without build metadata.</summary>
    public string Version { get; } = version;

    /// <summary>The database written to the settings file that the running host does not use yet; null when none.</summary>
    public DatabaseSettings? PendingDatabase
    {
        get
        {
            lock (_lock)
            {
                return _pendingDatabase;
            }
        }

        set
        {
            lock (_lock)
            {
                _pendingDatabase = value;
            }
        }
    }

    /// <summary>The informational version of the assembly named <paramref name="applicationName"/> (the host).</summary>
    public static string ReadVersion(string? applicationName)
    {
        Assembly? assembly = null;
        if (!string.IsNullOrWhiteSpace(applicationName))
        {
            try
            {
                assembly = Assembly.Load(new AssemblyName(applicationName));
            }
            catch (IOException)
            {
            }
            catch (BadImageFormatException)
            {
            }
        }

        assembly ??= Assembly.GetEntryAssembly() ?? typeof(SetupState).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
