using System.Reflection;
using NetArchTest.Rules;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Samples.Notifications;
using Vyxum.Samples.Orders;
using Xunit;

namespace Vyxum.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(OrdersModule).Assembly,
        typeof(NotificationsModule).Assembly,
    ];

    private static readonly Assembly Abstractions = typeof(IModule).Assembly;
    private static readonly Assembly Platform = typeof(ModuleCatalog).Assembly;
    private static readonly Assembly Host = typeof(Program).Assembly;

    private static bool IsModuleAssembly(string name) =>
        name.StartsWith("Vyxum.Modules.", StringComparison.Ordinal) ||
        name.StartsWith("Vyxum.Samples.", StringComparison.Ordinal);

    private static IEnumerable<string> VyxumReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Vyxum.", StringComparison.Ordinal));

    [Fact]
    public void Module_assemblies_are_found()
    {
        // Guards the other rules against passing on an empty set.
        ModuleAssemblies.Length.ShouldBeGreaterThan(1);
        ModuleAssemblies.ShouldAllBe(a => IsModuleAssembly(a.GetName().Name!));
    }

    [Fact]
    public void Modules_reference_only_the_abstractions_among_vyxum_assemblies()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            VyxumReferences(assembly)
                .Where(n => n != Abstractions.GetName().Name)
                .ShouldBeEmpty($"{assembly.GetName().Name} may only reference Vyxum.Platform.Abstractions");
        }
    }

    [Fact]
    public void Module_types_do_not_use_other_modules_or_the_platform_runtime()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var otherModules = ModuleAssemblies.Where(a => a != assembly).Select(a => a.GetName().Name!).ToArray();
            var result = Types.InAssembly(assembly)
                .ShouldNot().HaveDependencyOnAny([.. otherModules, "Vyxum.Host"])
                .GetResult();
            result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void Modules_do_not_reference_each_other()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            VyxumReferences(assembly)
                .Where(IsModuleAssembly)
                .ShouldBeEmpty($"{assembly.GetName().Name} must not reference another module");
        }
    }

    [Fact]
    public void Platform_and_abstractions_reference_no_module_assembly()
    {
        foreach (var assembly in new[] { Platform, Abstractions })
        {
            VyxumReferences(assembly)
                .Where(IsModuleAssembly)
                .ShouldBeEmpty($"{assembly.GetName().Name} must not reference a module");
        }
    }

    [Fact]
    public void Abstractions_do_not_reference_the_platform_runtime()
    {
        VyxumReferences(Abstractions).ShouldBeEmpty();
    }

    [Fact]
    public void Platform_types_do_not_use_sample_or_module_namespaces()
    {
        var result = Types.InAssemblies([Platform, Abstractions])
            .ShouldNot().HaveDependencyOnAny("Vyxum.Samples", "Vyxum.Modules")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Host_does_not_reference_sample_modules()
    {
        VyxumReferences(Host)
            .Where(n => n.StartsWith("Vyxum.Samples.", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }
}
