using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Vyxum.Platform;

/// <summary>
/// The shared settings file <c>vyxum.json</c> in the data directory (decision V-37). The installer and the
/// setup wizard write it; the web host, the agent and the desktop window read it. Unlike <c>appsettings.json</c>
/// in the program folder, it survives updates. Customers do not edit it by hand.
/// </summary>
public sealed class VyxumSettingsFile
{
    /// <summary>File name inside the data directory.</summary>
    public const string FileName = "vyxum.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly Lock WriteLock = new();

    /// <summary>Creates the accessor for the file at <paramref name="path"/>.</summary>
    public VyxumSettingsFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    /// <summary>Full path of the file.</summary>
    public string Path { get; }

    /// <summary>The file in the configured data directory (see <see cref="DatabaseSettings.GetDataDirectory"/>).</summary>
    public static VyxumSettingsFile FromConfiguration(IConfiguration configuration) =>
        new(System.IO.Path.Combine(DatabaseSettings.GetDataDirectory(configuration), FileName));

    /// <summary>
    /// Adds the file as an optional JSON source of <paramref name="configuration"/>: after the <c>appsettings</c>
    /// files, so it overrides them, and before environment variables and the command line, which still win.
    /// The location follows <c>Vyxum:DataDirectory</c> as configured so far. Changes are not reloaded.
    /// </summary>
    /// <returns>The file that was added.</returns>
    public static VyxumSettingsFile AddTo(IConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var file = FromConfiguration(configuration);
        // AddJsonFile resolves the file provider from the full path, also when the directory does not exist yet.
        var source = new ConfigurationBuilder()
            .AddJsonFile(file.Path, optional: true, reloadOnChange: false)
            .Sources[0];

        var sources = configuration.Sources;
        sources.Insert(InsertionIndex(sources), source);
        return file;
    }

    /// <summary>
    /// Sets or removes values and writes the file atomically. Keys use the configuration form
    /// (<c>Vyxum:Database:Provider</c>) and are stored as nested objects; a null value removes the key.
    /// Every other key in the file, including unknown ones, is kept.
    /// </summary>
    /// <exception cref="InvalidDataException">The existing file is not a JSON object.</exception>
    public void Update(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        lock (WriteLock)
        {
            var root = Read();
            foreach (var (key, value) in values)
            {
                var segments = key.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (segments.Length == 0)
                {
                    throw new ArgumentException("A settings key must not be empty.", nameof(values));
                }

                // A flat "A:B:C" property means the same key; drop it so the nested value is the only one.
                RemoveProperty(root, key);
                if (value is null)
                {
                    Remove(root, segments, 0);
                }
                else
                {
                    Set(root, segments, value);
                }
            }

            WriteAtomically(root.ToJsonString(WriteOptions));
        }
    }

    /// <summary>The file content as a JSON object; empty when the file does not exist.</summary>
    private JsonObject Read()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        var text = File.ReadAllText(Path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
                ?? throw new InvalidDataException($"The settings file '{Path}' does not contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The settings file '{Path}' is not valid JSON.", exception);
        }
    }

    /// <summary>Writes a temporary file next to the target, flushes it to disk and swaps it in.</summary>
    private void WriteAtomically(string content)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(Path))
            {
                File.Replace(temporary, Path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, Path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void Set(JsonObject root, string[] segments, string value)
    {
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var name = FindName(current, segments[i]);
            if (name is not null && current[name] is JsonObject child)
            {
                current = child;
                continue;
            }

            if (name is not null)
            {
                current.Remove(name);
            }

            var created = new JsonObject();
            current[segments[i]] = created;
            current = created;
        }

        var leaf = FindName(current, segments[^1]);
        if (leaf is not null)
        {
            current.Remove(leaf);
        }

        current[segments[^1]] = value;
    }

    /// <summary>Removes the key and then any object left empty on its path.</summary>
    private static void Remove(JsonObject node, string[] segments, int index)
    {
        var name = FindName(node, segments[index]);
        if (name is null)
        {
            return;
        }

        if (index == segments.Length - 1)
        {
            node.Remove(name);
            return;
        }

        if (node[name] is JsonObject child)
        {
            Remove(child, segments, index + 1);
            if (child.Count == 0)
            {
                node.Remove(name);
            }
        }
    }

    private static void RemoveProperty(JsonObject node, string name)
    {
        if (name.Contains(':', StringComparison.Ordinal) && FindName(node, name) is { } existing)
        {
            node.Remove(existing);
        }
    }

    /// <summary>Configuration keys are case-insensitive; finds the stored spelling of <paramref name="name"/>.</summary>
    private static string? FindName(JsonObject node, string name) =>
        node.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// After the last <c>appsettings*.json</c> source; without one, before the unprefixed environment variables;
    /// without those (defaults disabled), first, so that every explicitly added source wins.
    /// </summary>
    private static int InsertionIndex(IList<IConfigurationSource> sources)
    {
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (sources[i] is FileConfigurationSource { Path: { } path } &&
                System.IO.Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
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
