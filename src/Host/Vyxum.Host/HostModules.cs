using System.Reflection;

namespace Vyxum.Host;

/// <summary>Decides which assemblies the host loads modules from.</summary>
public static class HostModules
{
    /// <summary>Configuration section: a list of assembly names to load modules from, e.g. <c>Vyxum:Modules:0</c>.</summary>
    public const string ConfigurationKey = "Vyxum:Modules";

    /// <summary>Assemblies that are always loaded. Empty in F0: real modules arrive in later phases.</summary>
    public static IReadOnlyList<Assembly> BuiltIn { get; } = [];

    /// <summary>The built-in assemblies plus the assemblies named in configuration.</summary>
    public static IReadOnlyList<Assembly> Load(IConfiguration configuration)
    {
        var names = configuration.GetSection(ConfigurationKey).Get<string[]>() ?? [];
        return [.. BuiltIn, .. names.Select(name => Assembly.Load(new AssemblyName(name)))];
    }
}
