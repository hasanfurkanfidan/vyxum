namespace Vyxum.Platform;

/// <summary>Exponential backoff for failed outbox deliveries.</summary>
public static class RetryBackoff
{
    /// <summary>
    /// Delay after the <paramref name="failedAttempts"/>-th failure: <paramref name="baseDelay"/> × 2^(n−1),
    /// capped at <paramref name="maxDelay"/>.
    /// </summary>
    public static TimeSpan GetDelay(int failedAttempts, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (failedAttempts <= 1)
        {
            return baseDelay < maxDelay ? baseDelay : maxDelay;
        }

        var exponent = Math.Min(failedAttempts - 1, 30);
        var ticks = baseDelay.Ticks * Math.Pow(2, exponent);
        return ticks >= maxDelay.Ticks ? maxDelay : TimeSpan.FromTicks((long)ticks);
    }
}
