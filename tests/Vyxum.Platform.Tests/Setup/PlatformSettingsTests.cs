using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.Platform.Tests.Setup;

public class PlatformSettingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Settings_are_stored_updated_and_removed(DatabaseProvider provider)
    {
        var reason = TestDatabase.GetUnavailableReason(provider);
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        await using var database = TestDatabase.Create(provider);
        await using var host = await TestModuleHost.StartAsync(database);

        await using (var scope = host.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettings>();
            (await settings.GetAsync(PlatformSettingKeys.CompanyName, Ct)).ShouldBeNull();
            await settings.SetAsync(PlatformSettingKeys.CompanyName, "Örnek Gıda A.Ş.", Ct);
            await settings.SetAsync(PlatformSettingKeys.SetupCompletedAt, "2026-11-21T10:00:00.0000000Z", Ct);
        }

        await using (var scope = host.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettings>();
            (await settings.GetAsync(PlatformSettingKeys.CompanyName, Ct)).ShouldBe("Örnek Gıda A.Ş.");
            await settings.SetAsync(PlatformSettingKeys.CompanyName, "Yeni Ad", Ct);
            await settings.SetAsync(PlatformSettingKeys.SetupCompletedAt, null, Ct);
        }

        await using (var scope = host.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettings>();
            (await settings.GetAsync(PlatformSettingKeys.CompanyName, Ct)).ShouldBe("Yeni Ad");
            (await settings.GetAsync(PlatformSettingKeys.SetupCompletedAt, Ct)).ShouldBeNull();
        }
    }
}
