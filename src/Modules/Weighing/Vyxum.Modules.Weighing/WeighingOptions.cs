namespace Vyxum.Modules.Weighing;

/// <summary>Weighing rules, bound from <c>Vyxum:Weighing</c>. Nothing here is hard-coded elsewhere.</summary>
public sealed class WeighingOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Vyxum:Weighing";

    /// <summary>Only a stable weight can be recorded. Default true.</summary>
    public bool RequireStable { get; set; } = true;

    /// <summary>
    /// Smallest net weight in kg that can be recorded. The net must always be greater than 0;
    /// a positive value additionally requires net (converted to kg) &gt;= this value. Default 0.
    /// </summary>
    public decimal MinimumNet { get; set; }

    /// <summary>
    /// Oldest reading that can be recorded, measured from when the web application received it. The agent sends the
    /// latest reading at least once a second, so an older one means the agent or its network stopped while the
    /// connection still looks open. A record is then rejected with <c>weighing.NoReading</c>. Zero or less turns the
    /// check off. Default 3 seconds.
    /// </summary>
    public TimeSpan MaxReadingAge { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Longest time between the reading the operator saw (sent with the save) and the save reaching the server. A later
    /// save, for example one retried after a network error, is rejected with <c>weighing.ReadingChanged</c> so that it
    /// cannot record whatever is on the scale by then. Zero or less turns the check off. Default 10 seconds.
    /// </summary>
    public TimeSpan MaxSaveDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>A cancel needs a reason. Default true.</summary>
    public bool RequireCancelReason { get; set; } = true;

    /// <summary>Currency of amounts, stored on every record. Default <c>TRY</c>.</summary>
    public string Currency { get; set; } = "TRY";

    /// <summary>Decimals amounts are rounded to (0 to 4). Default 2.</summary>
    public int AmountDecimals { get; set; } = 2;

    /// <summary>How amounts are rounded. Default <see cref="MidpointRounding.AwayFromZero"/>.</summary>
    public MidpointRounding AmountRounding { get; set; } = MidpointRounding.AwayFromZero;
}
