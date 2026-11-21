using Shouldly;
using Xunit;

namespace Vyxum.Platform.Tests.Events;

public class RetryBackoffTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(9, 256)]
    [InlineData(10, 300)]
    [InlineData(1000, 300)]
    [InlineData(int.MaxValue, 300)]
    public void Delay_doubles_per_failure_up_to_the_cap(int failedAttempts, int expectedSeconds)
    {
        RetryBackoff.GetDelay(failedAttempts, Base, Max).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Base_delay_above_cap_is_capped()
    {
        RetryBackoff.GetDelay(1, TimeSpan.FromMinutes(10), Max).ShouldBe(Max);
    }
}
