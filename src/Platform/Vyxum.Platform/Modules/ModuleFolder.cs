using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Vyxum.Platform;

/// <summary>
/// Loads add-on module assemblies from a folder (decision V-59: the setup program puts the paid modules there).
/// Layout: one subfolder per module, named after its main assembly, holding the module's published output:
/// <c>modules\Vyxum.Modules.Labels\Vyxum.Modules.Labels.dll</c> plus its <c>.deps.json</c> and dependencies.
/// </summary>
/// <remarks>
/// Each module gets its own <see cref="AssemblyLoadContext"/>. An assembly the application already ships (the
/// platform abstractions, EF Core, ASP.NET Core and the rest of the trusted platform assemblies) is always taken from
/// the application, so modules and the platform share one copy of every shared type. Only what the application lacks
/// (a module's own libraries, native files and satellite resources) comes from the module's folder.
/// </remarks>
public static class ModuleFolder
{
    /// <summary>The folder name next to the executable: <c>modules</c>.</summary>
    public const string DefaultName = "modules";

    private static readonly Lazy<HashSet<string>> ApplicationAssemblies = new(ReadApplicationAssemblies);

    /// <summary>
    /// Loads the main assembly of every module subfolder of <paramref name="path"/>, ordered by folder name.
    /// A missing folder means no add-on modules. A subfolder without a <c>&lt;folder name&gt;.dll</c> is skipped.
    /// </summary>
    /// <param name="path">The modules folder.</param>
    /// <param name="skip">Assembly names already loaded another way (for example named in settings); not loaded again.</param>
    public static IReadOnlyList<Assembly> Load(string path, IEnumerable<string>? skip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path))
            return [];

        var skipped = new HashSet<string>(skip ?? [], StringComparer.OrdinalIgnoreCase);
        var result = new List<Assembly>();
        foreach (var directory in Directory.GetDirectories(path).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(directory);
            var main = Path.Combine(directory, name + ".dll");
            if (!File.Exists(main) || skipped.Contains(name))
                continue;

            var context = new ModuleLoadContext(main);
            result.Add(context.LoadFromAssemblyPath(main));
        }

        return result;
    }

    /// <summary>The simple names of the assemblies the application itself ships (the trusted platform assemblies).</summary>
    private static HashSet<string> ReadApplicationAssemblies()
    {
        var list = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return new HashSet<string>(
            list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(Path.GetFileNameWithoutExtension)!,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The load context of one module: the application's assemblies first, then the module's folder.</summary>
    private sealed class ModuleLoadContext(string mainAssemblyPath)
        : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainAssemblyPath))
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Returning null hands the request to the default context, which owns the application's assemblies.
            // Satellite resource assemblies are never shared: a module's own translations live in its folder.
            if (assemblyName.Name is { } simple && ApplicationAssemblies.Value.Contains(simple) && IsNeutral(assemblyName))
                return null;

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }

        private static bool IsNeutral(AssemblyName name) =>
            string.IsNullOrEmpty(name.CultureName) || name.CultureName.Equals("neutral", StringComparison.OrdinalIgnoreCase);
    }
}
