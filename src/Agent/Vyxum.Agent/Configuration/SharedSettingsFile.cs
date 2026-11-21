using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Vyxum.Agent.Configuration;

/// <summary>
/// The shared settings file <c>vyxum.json</c> in the data directory (decision V-37), which the installer and the
/// web setup write. The agent must not reference the platform, so this repeats the platform's path rule:
/// <c>Vyxum:DataDirectory</c> when configured, otherwise <c>%ProgramData%\Vyxum</c> on Windows and <c>./data</c> elsewhere.
/// </summary>
public static class SharedSettingsFile
{
    /// <summary>Configuration key of the data directory.</summary>
    public const string DataDirectoryKey = "Vyxum:DataDirectory";

    /// <summary>File name inside the data directory.</summary>
    public const string FileName = "vyxum.json";

    /// <summary>Full path of the file for <paramref name="configuration"/>.</summary>
    public static string GetPath(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration[DataDirectoryKey];
        var directory = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vyxum")
                : Path.GetFullPath("data");
        return Path.Combine(directory, FileName);
    }

    /// <summary>
    /// Adds the file as an optional JSON source after the <c>appsettings</c> file (so it overrides it) and before
    /// environment variables and the command line (which still win). Changes are not reloaded.
    /// </summary>
    public static void AddTo(IConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var source = new ConfigurationBuilder()
            .AddJsonFile(GetPath(configuration), optional: true, reloadOnChange: false)
            .Sources[0];
        var sources = configuration.Sources;
        sources.Insert(InsertionIndex(sources), source);
    }

    private static int InsertionIndex(IList<IConfigurationSource> sources)
    {
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (sources[i] is FileConfigurationSource { Path: { } path } && path != FileName)
            {
                return i + 1;
            }
        }

        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                return i;
            }
        }

        return 0;
    }
}
