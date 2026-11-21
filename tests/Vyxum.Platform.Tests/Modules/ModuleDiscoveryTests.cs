using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Platform.Tests.Modules;

public class ModuleDiscoveryTests
{
    [Fact]
    public void Discover_finds_public_concrete_modules_once_per_assembly()
    {
        var assembly = typeof(DiscoverableModule).Assembly;

        var modules = ModuleDiscovery.Discover([assembly, assembly]);

        modules.ShouldHaveSingleItem().ShouldBeOfType<DiscoverableModule>();
    }

    [Fact]
    public void AddVyxumPlatform_registers_catalog_and_capability_registry()
    {
        var services = new ServiceCollection();

        var catalog = services.AddVyxumPlatform(
            new ConfigurationBuilder().Build(),
            [TestModule.Create("a", provides: ["cap.a"]), TestModule.Create("b", optional: ["cap.missing"])]);

        using var provider = services.BuildServiceProvider();
        catalog.Modules.Select(m => m.Manifest.Id).ShouldBe(["a", "b"]);
        provider.GetRequiredService<ModuleCatalog>().ShouldBeSameAs(catalog);
        var registry = provider.GetRequiredService<ICapabilityRegistry>();
        registry.IsAvailable("cap.a").ShouldBeTrue();
        registry.IsAvailable("cap.missing").ShouldBeFalse();
    }

    [Fact]
    public void AddVyxumPlatform_fails_when_required_capability_is_missing()
    {
        var exception = Should.Throw<ModuleValidationException>(() => new ServiceCollection().AddVyxumPlatform(
            new ConfigurationBuilder().Build(),
            [TestModule.Create("b", requires: ["cap.missing"])]));

        exception.Message.ShouldContain("Module 'b' requires capability 'cap.missing'");
    }

    [Fact]
    public void Modules_see_capabilities_while_registering_services()
    {
        bool? seen = null;
        var consumer = new TestModule(
            new ModuleManifest { Id = "consumer", Version = new Version(1, 0), Optional = ["cap.a"] },
            (_, context) => seen = context.Capabilities.IsAvailable("cap.a"));

        new ServiceCollection().AddVyxumPlatform(
            new ConfigurationBuilder().Build(),
            [consumer, TestModule.Create("provider", provides: ["cap.a"])]);

        seen.ShouldBe(true);
    }
}

public sealed class DiscoverableModule : IModule
{
    public ModuleManifest Manifest { get; } = new() { Id = "tests.discoverable", Version = new Version(1, 0) };

    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
    }
}

public abstract class AbstractModule : IModule
{
    public abstract ModuleManifest Manifest { get; }

    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
    }
}
