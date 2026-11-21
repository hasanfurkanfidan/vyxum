using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Tests.Modules;
using Xunit;

namespace Vyxum.Platform.Tests.Ui;

public class UiStringCatalogTests
{
    // Resources/FallbackStrings.tr.resx deliberately lacks "OnlyEnglish".
    private static readonly ResourceManager Fallback =
        new("Vyxum.Platform.Tests.Ui.Resources.FallbackStrings", typeof(UiStringCatalogTests).Assembly);

    [Fact]
    public void Strings_are_prefixed_and_translated_with_english_fallback()
    {
        var catalog = new UiStringCatalog([new UiStringSource("products", Fallback)]);

        catalog.GetStrings(CultureInfo.GetCultureInfo("tr")).ShouldBe(new Dictionary<string, string>
        {
            ["products.OnlyEnglish"] = "Only in English",
            ["products.Title"] = "Ürünler",
        });
        catalog.GetStrings(CultureInfo.GetCultureInfo("en")).ShouldBe(new Dictionary<string, string>
        {
            ["products.OnlyEnglish"] = "Only in English",
            ["products.Title"] = "Products",
        });
    }

    [Fact]
    public void Duplicate_prefixes_are_rejected()
    {
        Should.Throw<InvalidOperationException>(() =>
                new UiStringCatalog([new UiStringSource("products", Fallback), new UiStringSource("products", Fallback)]))
            .Message.ShouldContain("'products'");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Products")]
    [InlineData("products.")]
    [InlineData("pro ducts")]
    public void Invalid_prefixes_are_rejected(string prefix)
    {
        Should.Throw<ArgumentException>(() => new UiStringSource(prefix, Fallback));
    }

    [Fact]
    public void A_module_registers_its_strings_while_configuring_services()
    {
        var module = new TestModule(
            new ModuleManifest { Id = "products", Version = new Version(1, 0) },
            (services, _) => services.AddUiStrings("products", Fallback));
        var services = new ServiceCollection();
        services.AddVyxumPlatform(new ConfigurationBuilder().Build(), [module]);
        using var provider = services.BuildServiceProvider();

        var strings = provider.GetRequiredService<UiStringCatalog>().GetStrings(CultureInfo.GetCultureInfo("tr"));

        strings["products.Title"].ShouldBe("Ürünler");
    }
}
