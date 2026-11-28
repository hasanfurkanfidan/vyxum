using System.Globalization;
using System.Runtime.Loader;
using Shouldly;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Platform.Tests.Modules;

/// <summary>
/// The sample paid module is not referenced by this test project: the build copies it to
/// <c>module-folder/Vyxum.Samples.Paid</c>, like the setup program puts a paid module in the host's modules folder.
/// </summary>
public sealed class ModuleFolderTests : IDisposable
{
    private const string SampleName = "Vyxum.Samples.Paid";
    private static readonly string SampleFolder = Path.Combine(AppContext.BaseDirectory, "module-folder");
    private readonly string _empty = Path.Combine(Path.GetTempPath(), "vyxum-modules-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_empty))
            Directory.Delete(_empty, recursive: true);
    }

    [Fact]
    public void Load_loads_a_module_folder_in_its_own_context_and_shares_the_platform_types()
    {
        var assembly = ModuleFolder.Load(SampleFolder).ShouldHaveSingleItem();

        assembly.GetName().Name.ShouldBe(SampleName);
        AssemblyLoadContext.GetLoadContext(assembly).ShouldNotBe(AssemblyLoadContext.Default);

        // The module implements the application's IModule, not a second copy of the abstractions.
        var module = ModuleDiscovery.Discover([assembly]).ShouldHaveSingleItem();
        module.Manifest.Id.ShouldBe("samples.paid");
        AssemblyLoadContext.GetLoadContext(typeof(IModule).Assembly).ShouldBe(AssemblyLoadContext.Default);
    }

    [Fact]
    public void Load_finds_the_module_own_translations_in_its_folder()
    {
        var assembly = ModuleFolder.Load(SampleFolder).ShouldHaveSingleItem();

        var satellite = assembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo("tr"));

        satellite.Location.ShouldStartWith(Path.Combine(SampleFolder, SampleName));
    }

    [Fact]
    public void Load_returns_nothing_when_the_folder_does_not_exist()
    {
        ModuleFolder.Load(_empty).ShouldBeEmpty();
    }

    [Fact]
    public void Load_skips_folders_without_a_main_assembly_and_assemblies_loaded_another_way()
    {
        Directory.CreateDirectory(Path.Combine(_empty, "Vyxum.Modules.Missing"));
        File.WriteAllText(Path.Combine(_empty, "Vyxum.Modules.Missing", "readme.txt"), "no assembly here");

        ModuleFolder.Load(_empty).ShouldBeEmpty();
        ModuleFolder.Load(SampleFolder, skip: [SampleName.ToUpperInvariant()]).ShouldBeEmpty();
    }
}
