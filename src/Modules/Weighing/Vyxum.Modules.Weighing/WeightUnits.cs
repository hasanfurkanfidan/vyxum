using System.Globalization;

namespace Vyxum.Modules.Weighing;

/// <summary>Weight units the module can convert: mg, g, kg, t, lb, oz (case-insensitive) and decimal helpers.</summary>
public static class WeightUnits
{
    private static readonly Dictionary<string, decimal> MilligramsPerUnit = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mg"] = 1m,
        ["g"] = 1_000m,
        ["kg"] = 1_000_000m,
        ["t"] = 1_000_000_000m,
        ["lb"] = 453_592.37m,
        ["oz"] = 28_349.523125m,
    };

    private const decimal MilligramsPerKilogram = 1_000_000m;

    /// <summary>The canonical (lowercase) unit for a known unit, the trimmed text for an unknown one, null for none.</summary>
    public static string? Normalize(string? unit)
    {
        var trimmed = unit?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return MilligramsPerUnit.ContainsKey(trimmed) ? trimmed.ToLowerInvariant() : trimmed;
    }

    /// <summary>True when <paramref name="unit"/> can be converted.</summary>
    public static bool IsKnown(string? unit) => unit is not null && MilligramsPerUnit.ContainsKey(unit.Trim());

    /// <summary>Milligrams in one <paramref name="unit"/>.</summary>
    /// <exception cref="ArgumentException">The unit is unknown.</exception>
    public static decimal ToMilligrams(string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return MilligramsPerUnit.TryGetValue(unit.Trim(), out var factor)
            ? factor
            : throw new ArgumentException($"Unknown weight unit '{unit}'.", nameof(unit));
    }

    /// <summary>Converts <paramref name="value"/> from <paramref name="unit"/> to kg.</summary>
    public static decimal ToKilograms(decimal value, string unit) => value * ToMilligrams(unit) / MilligramsPerKilogram;

    /// <summary>Converts <paramref name="kilograms"/> to <paramref name="unit"/>.</summary>
    public static decimal FromKilograms(decimal kilograms, string unit) => kilograms * MilligramsPerKilogram / ToMilligrams(unit);

    /// <summary>Removes trailing zeros: 1.2600 becomes 1.26.</summary>
    public static decimal TrimScale(decimal value) => value / 1.0000000000000000000000000000m;

    /// <summary>Formats <paramref name="value"/> with exactly <paramref name="scale"/> decimals, invariant culture.</summary>
    public static string Format(decimal value, int scale) =>
        decimal.Round(value, scale, MidpointRounding.AwayFromZero).ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>10 to the power of <paramref name="exponent"/>.</summary>
    public static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}
