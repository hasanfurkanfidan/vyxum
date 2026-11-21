namespace Vyxum.Devices;

/// <summary>Settings for a <see cref="DeviceRunner"/>.</summary>
public sealed record DeviceRunnerOptions
{
    /// <summary>The wait after the first failure. Doubles after each further failure. Default: 1 second.</summary>
    public TimeSpan InitialReconnectDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between attempts. Default: 30 seconds.</summary>
    public TimeSpan MaxReconnectDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(30);

    /// <summary>How many readings are buffered for a slow consumer. When full, the oldest is dropped. Default: 64.</summary>
    public int ReadingBufferCapacity
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 64;

    /// <summary>
    /// The wait after <paramref name="consecutiveFailures"/> failures in a row (1 for the first failure):
    /// initial delay doubled for each further failure, capped at the maximum.
    /// </summary>
    public TimeSpan GetReconnectDelay(int consecutiveFailures)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(consecutiveFailures, 1);
        var max = MaxReconnectDelay < InitialReconnectDelay ? InitialReconnectDelay : MaxReconnectDelay;
        var delay = InitialReconnectDelay;
        for (var i = 1; i < consecutiveFailures && delay < max; i++)
        {
            delay += delay;
        }

        return delay > max ? max : delay;
    }
}
