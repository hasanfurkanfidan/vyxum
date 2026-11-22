using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetArchTest.Rules;
using Shouldly;
using Vyxum.Agent;
using Vyxum.Devices;
using Vyxum.Devices.Discovery;
using Vyxum.Devices.GenericAscii;
using Vyxum.Devices.MtSics;
using Vyxum.Devices.Simulator;
using Vyxum.Devices.ToledoContinuous;
using Vyxum.Devices.Transports;
using Vyxum.Licensing;
using Vyxum.Modules.Products;
using Vyxum.Modules.Weighing;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Samples.Notifications;
using Vyxum.Samples.Orders;
using Vyxum.Samples.Paid;
using Xunit;

namespace Vyxum.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(OrdersModule).Assembly,
        typeof(NotificationsModule).Assembly,
        typeof(PaidModule).Assembly,
        typeof(ProductsModule).Assembly,
        typeof(WeighingModule).Assembly,
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
    private static readonly Assembly DevicesDiscovery = typeof(PortScanner).Assembly;
    private static readonly Assembly AgentAssembly = typeof(DeviceFleet).Assembly;
    private static readonly Assembly LicensingAssembly = typeof(LicenseReader).Assembly;

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
        var names = ModuleAssemblies.Select(a => a.GetName().Name!).ToArray();
        names.ShouldContain("Vyxum.Modules.Products");
        names.ShouldContain("Vyxum.Modules.Weighing");
    }

    [Fact]
    public void Each_module_owns_exactly_one_module_db_context()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            assembly.GetTypes()
                .Count(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(ModuleDbContext)))
                .ShouldBe(1, $"{assembly.GetName().Name} must define exactly one ModuleDbContext");
        }
    }

    [Fact]
    public void Module_tables_start_with_their_module_prefix()
    {
        // The module tables share one database without schemas, so the prefix is what keeps them apart.
        AssertTablesPrefixed<ProductsDbContext>();
        AssertTablesPrefixed<WeighingDbContext>();
        AssertTablesPrefixed<PaidDbContext>();
    }

    private static void AssertTablesPrefixed<TContext>() where TContext : ModuleDbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options;
        using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        var prefix = context.TablePrefix + "_";
        var tables = context.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .OfType<string>()
            .ToArray();

        // Positive check first, so that the prefix check cannot pass vacuously.
        tables.ShouldNotBeEmpty(typeof(TContext).Name);
        tables.ShouldAllBe(name => name.StartsWith(prefix, StringComparison.Ordinal), $"every table of {typeof(TContext).Name} starts with {prefix}");
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

    [Fact]
    public void Discovery_references_only_device_assemblies()
    {
        // Positive check first: discovery uses the transports and the protocol probes.
        VyxumReferences(DevicesDiscovery).ShouldContain(DevicesTransports.GetName().Name!);
        VyxumReferences(DevicesDiscovery).ShouldContain(DevicesMtSics.GetName().Name!);

        VyxumReferences(DevicesDiscovery)
            .ShouldAllBe(n => IsDeviceAssembly(n) && n != DevicesSimulator.GetName().Name, "Vyxum.Devices.Discovery may only reference device assemblies, not the simulator");
    }

    [Fact]
    public void Platform_and_host_do_not_reference_discovery()
    {
        foreach (var assembly in new[] { Platform, Abstractions, Host })
        {
            VyxumReferences(assembly).ShouldNotContain(DevicesDiscovery.GetName().Name!);
        }
    }

    /// <summary>
    /// Only read commands are ever sent to a scale. The web application validates device settings against its own copy
    /// of the agent's lists (it may not reference the device assemblies), so the copies must stay equal.
    /// </summary>
    [Fact]
    public void Web_and_agent_allow_the_same_read_commands()
    {
        DeviceConfigLimits.GenericAsciiRequestCommands.ShouldBe(GenericAsciiOptions.AllowedRequestCommands);
        DeviceConfigLimits.MtSicsReadCommands.ShouldBe(MtSicsProtocolOptions.AllowedCommands);
    }

    [Fact]
    public void Licensing_references_no_other_vyxum_assembly()
    {
        // Licensing is shared with the license tool, so it stays free of the platform, modules, devices and the agent.
        LicensingAssembly.GetExportedTypes().ShouldNotBeEmpty();
        VyxumReferences(LicensingAssembly).ShouldBeEmpty();
    }

    [Fact]
    public void Abstractions_and_modules_do_not_reference_licensing()
    {
        foreach (var assembly in ModuleAssemblies.Append(Abstractions))
        {
            VyxumReferences(assembly).ShouldNotContain(LicensingAssembly.GetName().Name!, $"{assembly.GetName().Name} must not reference Vyxum.Licensing");
        }
    }

    private static bool IsDeviceAssembly(string name) =>
        name.StartsWith("Vyxum.Devices", StringComparison.Ordinal);
}
