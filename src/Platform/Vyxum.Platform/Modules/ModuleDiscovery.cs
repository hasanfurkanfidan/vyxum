using System.Reflection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>Finds and instantiates <see cref="IModule"/> implementations in assemblies.</summary>
public static class ModuleDiscovery
{
    /// <summary>
    /// Returns one instance of every public, non-abstract <see cref="IModule"/> type in the given assemblies.
    /// </summary>
    /// <exception cref="InvalidOperationException">A module type has no public parameterless constructor.</exception>
    public static IReadOnlyList<IModule> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var moduleTypes = assemblies
            .Distinct()
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        var withoutConstructor = moduleTypes.Where(t => t.GetConstructor(Type.EmptyTypes) is null).ToList();
        if (withoutConstructor.Count > 0)
        {
            throw new InvalidOperationException(
                "Module types need a public parameterless constructor: " +
                string.Join(", ", withoutConstructor.Select(t => t.FullName)));
        }

        return moduleTypes.Select(t => (IModule)Activator.CreateInstance(t)!).ToList();
    }
}
