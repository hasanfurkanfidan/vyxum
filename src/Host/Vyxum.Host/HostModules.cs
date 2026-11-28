using System.Reflection;
using Vyxum.Modules.Products;
using Vyxum.Modules.Weighing;
using Vyxum.Platform;

namespace Vyxum.Host;

/// <summary>Decides which assemblies the host loads modules from.</summary>
public static class HostModules
{
    /// <summary>Configuration section: a list of assembly names to load modules from, e.g. <c>Vyxum:Modules:0</c>.</summary>
    public const string ConfigurationKey = "Vyxum:Modules";

    /// <summary>
    /// Configuration key of the add-on modules folder. Default: the <c>modules</c> folder next to the executable,
    /// where the setup program puts the paid modules (decision V-59).
    /// </summary>
    public const string FolderKey = "Vyxum:ModulesPath";

    /// <summary>Open-core module assemblies that are always loaded. Adding a module is one line here plus a project reference.</summary>
    public static IReadOnlyList<Assembly> BuiltIn { get; } =
    [
        typeof(ProductsModule).Assembly,
        typeof(WeighingModule).Assembly,
    ];

    /// <summary>The built-in assemblies, the assemblies named in configuration and the modules found in the modules folder.</summary>
    public static IReadOnlyList<Assembly> Load(IConfiguration configuration)
    {
        var names = configuration.GetSection(ConfigurationKey).Get<string[]>() ?? [];
        var folder = configuration[FolderKey] is { Length: > 0 } configured
            ? Path.GetFullPath(configured, AppContext.BaseDirectory)
            : Path.Combine(AppContext.BaseDirectory, ModuleFolder.DefaultName);
        return
        [
            .. BuiltIn,
            .. names.Select(name => Assembly.Load(new AssemblyName(name))),
            .. ModuleFolder.Load(folder, names),
        ];
    }
}
