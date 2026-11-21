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

    /// <summary>A cancel needs a reason. Default true.</summary>
    public bool RequireCancelReason { get; set; } = true;

    /// <summary>Currency of amounts, stored on every record. Default <c>TRY</c>.</summary>
    public string Currency { get; set; } = "TRY";

    /// <summary>Decimals amounts are rounded to (0 to 4). Default 2.</summary>
    public int AmountDecimals { get; set; } = 2;

    /// <summary>How amounts are rounded. Default <see cref="MidpointRounding.AwayFromZero"/>.</summary>
    public MidpointRounding AmountRounding { get; set; } = MidpointRounding.AwayFromZero;
}
