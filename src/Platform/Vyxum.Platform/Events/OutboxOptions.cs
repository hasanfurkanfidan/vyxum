namespace Vyxum.Platform;

/// <summary>Outbox dispatcher settings, bound from the <c>Vyxum:Outbox</c> configuration section.</summary>
public sealed class OutboxOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Vyxum:Outbox";

    /// <summary>How often the outboxes are polled. Default 500 ms.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum messages read from one module's outbox per poll. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Delay before the first retry; doubles on every further failure. Default 1 s.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper limit of the retry delay. Default 5 min.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(5);
}
