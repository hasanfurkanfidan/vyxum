using System.Reflection;
using NetArchTest.Rules;
using Shouldly;
using Vyxum.Agent;
using Vyxum.Devices;
using Vyxum.Devices.GenericAscii;
using Vyxum.Devices.MtSics;
using Vyxum.Devices.Simulator;
using Vyxum.Devices.ToledoContinuous;
using Vyxum.Devices.Transports;
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

    private static readonly Assembly DevicesCore = typeof(IScaleProtocol).Assembly;
    private static readonly Assembly DevicesTransports = typeof(TcpTransport).Assembly;
    private static readonly Assembly DevicesSimulator = typeof(SimulatorTcpServer).Assembly;
    private static readonly Assembly DevicesMtSics = typeof(MtSicsProtocol).Assembly;
    private static readonly Assembly DevicesToledoContinuous = typeof(ToledoContinuousProtocol).Assembly;
    private static readonly Assembly DevicesGenericAscii = typeof(GenericAsciiProtocol).Assembly;
    private static readonly Assembly AgentAssembly = typeof(DeviceFleet).Assembly;

    private static readonly Assembly[] ProtocolAssemblies = [DevicesMtSics, DevicesToledoContinuous, DevicesGenericAscii];

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

    [Fact]
    public void Device_assemblies_are_found()
    {
        // Guards the device rules against passing on an empty set.
        var deviceAssemblies = new[] { DevicesCore, DevicesTransports, DevicesSimulator }.Concat(ProtocolAssemblies).ToArray();
        deviceAssemblies.Length.ShouldBe(6);
        deviceAssemblies.ShouldAllBe(a => IsDeviceAssembly(a.GetName().Name!));
        deviceAssemblies.ShouldAllBe(a => a.GetExportedTypes().Length > 0);
        // Every adapter must use the core contracts, otherwise its reference set would be empty.
        foreach (var assembly in deviceAssemblies.Where(a => a != DevicesCore))
        {
            VyxumReferences(assembly).ShouldContain(DevicesCore.GetName().Name!, $"{assembly.GetName().Name} must reference Vyxum.Devices");
        }
    }

    [Fact]
    public void Vyxum_devices_references_no_other_vyxum_assembly()
    {
        VyxumReferences(DevicesCore).ShouldBeEmpty();
    }

    [Fact]
    public void Protocol_assemblies_reference_only_vyxum_devices()
    {
        foreach (var assembly in ProtocolAssemblies)
        {
            VyxumReferences(assembly)
                .ShouldAllBe(n => n == DevicesCore.GetName().Name, $"{assembly.GetName().Name} may only reference Vyxum.Devices");
        }
    }

    [Fact]
    public void Transports_and_simulator_reference_only_vyxum_devices()
    {
        foreach (var assembly in new[] { DevicesTransports, DevicesSimulator })
        {
            VyxumReferences(assembly)
                .ShouldAllBe(n => n == DevicesCore.GetName().Name, $"{assembly.GetName().Name} may only reference Vyxum.Devices");
        }
    }

    [Fact]
    public void Platform_and_host_reference_no_device_assembly_or_agent()
    {
        // Positive checks first, so that the negative checks cannot pass vacuously.
        VyxumReferences(Platform).ShouldContain(Abstractions.GetName().Name!);
        VyxumReferences(Host).ShouldContain(Platform.GetName().Name!);

        foreach (var assembly in new[] { Platform, Abstractions, Host })
        {
            VyxumReferences(assembly)
                .Where(n => IsDeviceAssembly(n) || n == AgentAssembly.GetName().Name)
                .ShouldBeEmpty($"{assembly.GetName().Name} must not reference devices or the agent");
        }
    }

    [Fact]
    public void Agent_references_no_platform_assembly_or_host()
    {
        // Positive check first, so that the negative check cannot pass vacuously.
        VyxumReferences(AgentAssembly).ShouldContain(DevicesCore.GetName().Name!);

        VyxumReferences(AgentAssembly)
            .Where(n => n.StartsWith("Vyxum.Platform", StringComparison.Ordinal) || n == Host.GetName().Name)
            .ShouldBeEmpty("Vyxum.Agent must not reference the platform or the host");
    }

    private static bool IsDeviceAssembly(string name) =>
        name.StartsWith("Vyxum.Devices", StringComparison.Ordinal);
}
