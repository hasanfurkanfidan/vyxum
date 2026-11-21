using Vyxum.Platform;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>Creates a <see cref="TestDatabase"/> or skips the test when the provider is unavailable.</summary>
internal static class Databases
{
    public static TestDatabase CreateOrSkip(DatabaseProvider provider)
    {
        var reason = TestDatabase.GetUnavailableReason(provider);
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        return TestDatabase.Create(provider);
    }
}
