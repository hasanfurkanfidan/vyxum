using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Licensing;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Tests.Modules;
using Xunit;

namespace Vyxum.Platform.Tests.Licensing;

public class LicenseRegistrationTests
{
    [Fact]
    public void A_module_cannot_replace_the_trusted_key_ring()
    {
        using var foreignKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var foreignRing = new LicenseKeyRing([KeyValuePair.Create("foreign", foreignKey.ExportSubjectPublicKeyInfo())]);
        var module = new TestModule(
            new ModuleManifest { Id = "intruder", Version = new Version(1, 0) },
            (services, _) =>
            {
                services.AddSingleton<ILicenseKeyRing>(foreignRing);
                services.AddSingleton<ILicenseKeyRing>(_ => foreignRing);
            });
        var services = new ServiceCollection();

        services.AddVyxumPlatform(new ConfigurationBuilder().Build(), [module]);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILicenseKeyRing>().ShouldBeSameAs(LicenseKeyRing.Trusted);
        provider.GetServices<ILicenseKeyRing>().ShouldHaveSingleItem().ShouldBeSameAs(LicenseKeyRing.Trusted);
        provider.GetRequiredService<ILicenseKeyRing>().TryGetPublicKey("foreign", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_registration_after_the_platform_replaces_the_key_ring_for_tests()
    {
        using var testKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var testRing = new LicenseKeyRing([KeyValuePair.Create("test", testKey.ExportSubjectPublicKeyInfo())]);
        var services = new ServiceCollection();

        services.AddVyxumPlatform(new ConfigurationBuilder().Build(), Array.Empty<IModule>());
        services.AddSingleton<ILicenseKeyRing>(testRing);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILicenseKeyRing>().ShouldBeSameAs(testRing);
    }
}
