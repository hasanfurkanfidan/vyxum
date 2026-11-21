using System.Reflection;
using Vyxum.Modules.Products;
using Vyxum.Modules.Weighing;

namespace Vyxum.Host;

/// <summary>Decides which assemblies the host loads modules from.</summary>
public static class HostModules
{
    /// <summary>Configuration section: a list of assembly names to load modules from, e.g. <c>Vyxum:Modules:0</c>.</summary>
    public const string ConfigurationKey = "Vyxum:Modules";

    /// <summary>Open-core module assemblies that are always loaded. Adding a module is one line here plus a project reference.</summary>
    public static IReadOnlyList<Assembly> BuiltIn { get; } =
    [
        typeof(ProductsModule).Assembly,
        typeof(WeighingModule).Assembly,
    ];

    /// <summary>The built-in assemblies plus the assemblies named in configuration.</summary>
    public static IReadOnlyList<Assembly> Load(IConfiguration configuration)
    {
        var names = configuration.GetSection(ConfigurationKey).Get<string[]>() ?? [];
        return [.. BuiltIn, .. names.Select(name => Assembly.Load(new AssemblyName(name)))];
    }
}
