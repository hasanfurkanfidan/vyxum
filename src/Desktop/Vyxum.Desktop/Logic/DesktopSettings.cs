using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vyxum.Desktop;

/// <summary>
/// What the desktop window needs from the shared settings file (<c>%ProgramData%\Vyxum\vyxum.json</c>) and the command line.
/// Pure code with no Windows dependency so that it is tested on every platform.
/// </summary>
public sealed partial record DesktopSettings(Uri Url, bool FullScreen)
{
    /// <summary>Address used when neither the command line nor the settings file gives one.</summary>
    public const string DefaultUrl = "http://localhost:5000";

    /// <summary>Path of the shared settings file.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vyxum", "vyxum.json");

    /// <summary>Reads the settings file (a missing or unreadable file means defaults) and applies the command line.</summary>
    public static DesktopSettings Load(string[] args, string? path = null)
    {
        string? json = null;
        try
        {
            path ??= DefaultPath;
            if (File.Exists(path))
            {
                json = File.ReadAllText(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Parse(json, args);
    }

    /// <summary>Builds the settings from the file content (may be null) and the command line. Never throws on bad input.</summary>
    public static DesktopSettings Parse(string? json, string[] args)
    {
        string? urls = null;
        var fullScreen = false;

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    urls = ReadString(root, "Urls");
                    fullScreen = ReadBool(root, "Vyxum", "Desktop", "FullScreen")
                        ?? ReadFlatBool(root, "Vyxum:Desktop:FullScreen")
                        ?? false;
                }
            }
            catch (JsonException)
            {
            }
        }

        var url = ParseUrls(GetArgument(args, "--url")) ?? ParseUrls(urls) ?? new Uri(DefaultUrl);
        return new DesktopSettings(url, fullScreen);
    }

    /// <summary>
    /// Takes the first usable address of an ASP.NET Core style <c>Urls</c> value (several separated by ';').
    /// Wildcard and any-address hosts (<c>*</c>, <c>+</c>, <c>0.0.0.0</c>, <c>[::]</c>) become <c>localhost</c>.
    /// </summary>
    public static Uri? ParseUrls(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return null;
        }

        foreach (var part in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = AddressPattern().Match(part);
            if (!match.Success)
            {
                continue;
            }

            var host = match.Groups["host"].Value;
            if (host is "*" or "+" or "0.0.0.0" or "[::]" or "::")
            {
                host = "localhost";
            }

            var port = match.Groups["port"].Success ? ":" + match.Groups["port"].Value : string.Empty;
            if (Uri.TryCreate($"{match.Groups["scheme"].Value.ToLowerInvariant()}://{host}{port}/", UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    /// <summary>True when both addresses have the same scheme, host and port.</summary>
    public static bool IsSameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
        && a.Port == b.Port;

    private static string? GetArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }

            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(name.Length + 1)..];
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? ReadFlatBool(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) ? AsBool(value) : null;

    private static bool? ReadBool(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        return AsBool(current);
    }

    private static bool? AsBool(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
        _ => null,
    };

    [GeneratedRegex(@"^(?<scheme>https?)://(?<host>\[[^\]]+\]|[^/:\s]+)(?::(?<port>\d{1,5}))?(?:/.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AddressPattern();
}
