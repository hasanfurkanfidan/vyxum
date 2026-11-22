using System.Globalization;
using System.Resources;
using Shouldly;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Tests.Modules;
using Xunit;

namespace Vyxum.Platform.Tests.Ui;

public class ModuleDisplayNamesTests
{
    private static readonly ResourceManager Strings =
        new("Vyxum.Platform.Tests.Ui.Resources.FallbackStrings", typeof(ModuleDisplayNamesTests).Assembly);

    private static ModuleDisplayNames Create(string? displayNameKey, out ModuleManifest manifest)
    {
        manifest = new ModuleManifest { Id = "demo", Version = new Version(1, 0), DisplayNameKey = displayNameKey };
        var catalog = new ModuleCatalog([new ModuleDescriptor(new TestModule(manifest))]);
        return new ModuleDisplayNames(catalog, new UiStringCatalog([new UiStringSource("demo", Strings)]));
    }

    [Fact]
    public void Resolves_the_name_in_the_culture_and_returns_null_without_a_key()
    {
        var names = Create("demo.Title", out var manifest);
        names.Resolve(manifest, CultureInfo.GetCultureInfo("tr")).ShouldBe("Ürünler");
        names.Resolve(manifest, CultureInfo.GetCultureInfo("en")).ShouldBe("Products");

        var none = Create(null, out var plain);
        none.Resolve(plain, CultureInfo.GetCultureInfo("tr")).ShouldBeNull();
    }

    [Fact]
    public async Task Startup_fails_for_a_key_no_string_set_has()
    {
        var names = Create("demo.Typo", out _);
        var error = await Should.ThrowAsync<InvalidOperationException>(() => names.StartAsync(CancellationToken.None));
        error.Message.ShouldContain("demo.Typo");

        await Create("demo.Title", out _).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Preview_description_is_resolved_and_a_missing_key_fails_at_startup()
    {
        ModuleManifest Manifest(string key) => new()
        {
            Id = "demo",
            Version = new Version(1, 0),
            Preview = new ModulePreview { DescriptionKey = key, Image = "screen.svg" },
        };

        var good = Manifest("demo.Title");
        var catalog = new ModuleCatalog([new ModuleDescriptor(new TestModule(good))]);
        var names = new ModuleDisplayNames(catalog, new UiStringCatalog([new UiStringSource("demo", Strings)]));
        names.ResolveDescription(good, CultureInfo.GetCultureInfo("tr")).ShouldBe("Ürünler");
        await names.StartAsync(CancellationToken.None);

        var bad = Manifest("demo.PreviewTypo");
        var badNames = new ModuleDisplayNames(
            new ModuleCatalog([new ModuleDescriptor(new TestModule(bad))]),
            new UiStringCatalog([new UiStringSource("demo", Strings)]));
        var error = await Should.ThrowAsync<InvalidOperationException>(() => badNames.StartAsync(CancellationToken.None));
        error.Message.ShouldContain("demo.PreviewTypo");

        var none = new ModuleManifest { Id = "plain", Version = new Version(1, 0) };
        names.ResolveDescription(none, CultureInfo.GetCultureInfo("tr")).ShouldBeNull();
    }
}
