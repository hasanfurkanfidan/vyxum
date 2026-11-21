using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vyxum.Devices.GenericAscii;

/// <summary>
/// Turns ASCII lines into readings with the rules of a <see cref="GenericAsciiOptions"/>. Never throws on a line:
/// a line that does not match, has no usable weight or takes too long to match yields null.
/// </summary>
/// <remarks>
/// State rules, in order: overload, underload (no value), motion, stable. When stable or motion patterns are configured
/// but none matches, the state is <see cref="WeightState.Motion"/> if a stable pattern exists and
/// <see cref="WeightState.Stable"/> otherwise. With no stability pattern at all the state is always stable.
/// </remarks>
public sealed class GenericAsciiParser
{
    /// <summary>The time a single regex match may take before the line is skipped.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private const RegexOptions Options = RegexOptions.CultureInvariant;
    private const NumberStyles ValueStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign | NumberStyles.AllowDecimalPoint;

    private readonly GenericAsciiOptions _options;
    private readonly Regex _pattern;
    private readonly Regex? _stable;
    private readonly Regex? _motion;
    private readonly Regex? _overload;
    private readonly Regex? _underload;
    private readonly Regex? _net;
    private readonly bool _hasUnit;
    private readonly bool _hasSign;
    private readonly bool _hasStatus;

    /// <summary>Validates <paramref name="options"/> and compiles its patterns. Throws <see cref="ArgumentException"/> on invalid settings.</summary>
    public GenericAsciiParser(GenericAsciiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _pattern = Compile(options.Pattern, nameof(options.Pattern))!;
        var groups = _pattern.GetGroupNames();
        if (!groups.Contains("weight", StringComparer.Ordinal))
        {
            throw new ArgumentException("Pattern must have a named group 'weight'.", nameof(options));
        }

        _hasUnit = groups.Contains("unit", StringComparer.Ordinal);
        _hasSign = groups.Contains("sign", StringComparer.Ordinal);
        _hasStatus = groups.Contains("status", StringComparer.Ordinal);
        _stable = Compile(options.StablePattern, nameof(options.StablePattern));
        _motion = Compile(options.MotionPattern, nameof(options.MotionPattern));
        _overload = Compile(options.OverloadPattern, nameof(options.OverloadPattern));
        _underload = Compile(options.UnderloadPattern, nameof(options.UnderloadPattern));
        _net = Compile(options.NetPattern, nameof(options.NetPattern));
    }

    /// <summary>Parses one line without its terminator. Returns null when the line does not give a reading.</summary>
    public WeightReading? Parse(string? line, DateTimeOffset timestamp)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        try
        {
            return ParseCore(line, timestamp);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private WeightReading? ParseCore(string line, DateTimeOffset timestamp)
    {
        var match = _pattern.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var statusGroup = _hasStatus ? match.Groups["status"] : null;
        var statusText = statusGroup is { Success: true } ? statusGroup.Value : line;
        var unit = _hasUnit && match.Groups["unit"] is { Success: true } u && u.Value.Trim() is { Length: > 0 } trimmed
            ? trimmed
            : _options.DefaultUnit;
        bool? isNet = _net is null ? null : _net.IsMatch(statusText);

        if (IsMatch(_overload, statusText))
        {
            return new WeightReading(null, unit, WeightState.Overload, null, isNet, line, timestamp);
        }

        if (IsMatch(_underload, statusText))
        {
            return new WeightReading(null, unit, WeightState.Underload, null, isNet, line, timestamp);
        }

        if (!TryParseWeight(match.Groups["weight"], out var value))
        {
            return null;
        }

        if (_hasSign && match.Groups["sign"] is { Success: true } sign && sign.Value.Trim() == "-" && value > 0)
        {
            value = -value;
        }

        WeightState state;
        if (IsMatch(_motion, statusText))
        {
            state = WeightState.Motion;
        }
        else if (IsMatch(_stable, statusText))
        {
            state = WeightState.Stable;
        }
        else
        {
            state = _stable is null ? WeightState.Stable : WeightState.Motion;
        }

        return new WeightReading(value, unit, state, null, isNet, line, timestamp);
    }

    private bool TryParseWeight(Group group, out decimal value)
    {
        value = 0;
        if (!group.Success)
        {
            return false;
        }

        var text = new StringBuilder(group.Value.Length);
        foreach (var c in group.Value)
        {
            if (!char.IsWhiteSpace(c))
            {
                text.Append(c);
            }
        }

        if (_options.ThousandsSeparator is { } thousands)
        {
            text.Replace(thousands, string.Empty);
        }

        if (_options.DecimalSeparator == ",")
        {
            // A dot cannot be a decimal point here; refuse it rather than misread the value.
            if (text.ToString().Contains('.', StringComparison.Ordinal))
            {
                return false;
            }

            text.Replace(',', '.');
        }

        return decimal.TryParse(text.ToString(), ValueStyle, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsMatch(Regex? regex, string text) => regex is not null && regex.IsMatch(text);

    private static Regex? Compile(string? pattern, string name)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        try
        {
            return new Regex(pattern, Options, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"{name} is not a valid regular expression: {ex.Message}", name, ex);
        }
    }
}
