using System.Reflection;
using System.Security.Cryptography;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// The embedded front end files of the loaded modules (<see cref="ModuleUi"/>), by module id and path.
/// </summary>
/// <remarks>
/// Only files embedded under <see cref="ModuleUi.ResourcePrefix"/> exist; a path is looked up as a whole, so there is
/// no directory listing and no way to reach another resource or a file on disk. Files are read once and kept in memory
/// (a module's screens are small).
/// </remarks>
public sealed class ModuleUiFiles
{
    private readonly Dictionary<string, ModuleUiAssembly> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ModuleUiAssembly> _previews = new(StringComparer.Ordinal);

    /// <summary>Indexes the front end files of every module in <paramref name="catalog"/> that declares a <see cref="ModuleUi"/>.</summary>
    public ModuleUiFiles(ModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var module in catalog.Modules.Where(m => m.Manifest.Ui is not null))
        {
            _modules[module.Manifest.Id] = new ModuleUiAssembly(module.Module.GetType().Assembly, ModuleUi.ResourcePrefix);
        }

        foreach (var module in catalog.Modules.Where(m => m.Manifest.Preview is not null))
        {
            _previews[module.Manifest.Id] = new ModuleUiAssembly(module.Module.GetType().Assembly, ModulePreview.ResourcePrefix);
        }
    }

    /// <summary>True when the module with <paramref name="moduleId"/> is loaded and has screens.</summary>
    public bool HasUi(string moduleId) => _modules.ContainsKey(moduleId);

    /// <summary>The file at <paramref name="path"/> of the module's front end, or null when there is none.</summary>
    public ModuleUiFile? Find(string moduleId, string path) =>
        _modules.TryGetValue(moduleId, out var module) ? module.Find(path) : null;

    /// <summary>True when the module with <paramref name="moduleId"/> is loaded and declares a preview.</summary>
    public bool HasPreview(string moduleId) => _previews.ContainsKey(moduleId);

    /// <summary>
    /// The file at <paramref name="path"/> of the module's preview (<see cref="ModulePreview.ResourcePrefix"/>), or null.
    /// Looks only at preview resources, never at the front end files.
    /// </summary>
    public ModuleUiFile? FindPreview(string moduleId, string path) =>
        _previews.TryGetValue(moduleId, out var module) ? module.Find(path) : null;

    /// <summary>Paths (relative to <see cref="ModuleUi.ResourcePrefix"/>, with <c>/</c>) of the front end files in <paramref name="assembly"/>.</summary>
    public static IReadOnlyDictionary<string, string> Index(Assembly assembly) => Index(assembly, ModuleUi.ResourcePrefix);

    /// <summary>Paths (relative to <see cref="ModulePreview.ResourcePrefix"/>, with <c>/</c>) of the preview files in <paramref name="assembly"/>.</summary>
    public static IReadOnlyDictionary<string, string> IndexPreview(Assembly assembly) => Index(assembly, ModulePreview.ResourcePrefix);

    private static Dictionary<string, string> Index(Assembly assembly, string prefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        // The build writes the recursive folder with the platform's separator; accept both.
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var normalized = name.Replace('\\', '/');
            if (normalized.StartsWith(prefix, StringComparison.Ordinal) && normalized.Length > prefix.Length)
            {
                result[normalized[prefix.Length..]] = name;
            }
        }

        return result;
    }

    private sealed class ModuleUiAssembly(Assembly assembly, string prefix)
    {
        private readonly IReadOnlyDictionary<string, string> _resources = Index(assembly, prefix);
        private readonly Dictionary<string, ModuleUiFile> _files = new(StringComparer.Ordinal);
        private readonly Lock _lock = new();

        public ModuleUiFile? Find(string path)
        {
            if (!_resources.TryGetValue(path, out var resourceName))
            {
                return null;
            }

            lock (_lock)
            {
                if (!_files.TryGetValue(path, out var file))
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName)
                        ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' could not be read.");
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    var content = memory.ToArray();
                    var etag = "\"" + Convert.ToHexStringLower(SHA256.HashData(content).AsSpan(0, 16)) + "\"";
                    file = new ModuleUiFile(content, ModuleUiContentTypes.For(path), etag);
                    _files[path] = file;
                }

                return file;
            }
        }
    }
}

/// <summary>A front end file of a module.</summary>
/// <param name="Content">The file's bytes.</param>
/// <param name="ContentType">The response content type.</param>
/// <param name="ETag">A strong entity tag of the content, with quotes.</param>
public sealed record ModuleUiFile(byte[] Content, string ContentType, string ETag);

/// <summary>Content types of the files a module's front end may contain.</summary>
internal static class ModuleUiContentTypes
{
    public static string For(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".map" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        _ => "application/octet-stream",
    };
}
