using System.Text;
using System.Text.RegularExpressions;

namespace Vyxum.Devices.GenericAscii;

/// <summary>
/// Recognises a plain ASCII weight output in bytes a device sent on its own: printable lines that end with a number and
/// an optional unit (for example <c>+0001.250 kg</c> or <c>ST,GS,  572 g</c>), repeated. Proposes a
/// <see cref="GenericAsciiOptions"/> pattern for them. Listen-only: nothing is written.
/// </summary>
public sealed partial class GenericAsciiProbe : IPassiveProbe
{
    /// <summary>The pattern proposed for a recognised output: the last number on the line, its sign and an optional unit.</summary>
    public const string ProposedPattern = @"^\D*?(?<sign>[-+])?\s*(?<weight>\d+(?:[.,]\d+)?)\s*(?<unit>[A-Za-z]{1,3})?\s*$";

    private const int MinimumLines = 2;

    /// <inheritdoc />
    public ProbeMatch? Recognise(ReadOnlySpan<byte> received, DateTimeOffset timestamp)
    {
        var lines = SplitLines(received);

        // The first piece may be the tail of a line that started before listening began.
        if (lines.Count > MinimumLines)
        {
            lines.RemoveAt(0);
        }

        if (lines.Count < MinimumLines || lines.Any(l => !IsPrintable(l)))
        {
            return null;
        }

        var matches = lines.Select(l => LinePattern().Match(l)).Where(m => m.Success).ToList();
        if (matches.Count < MinimumLines || matches.Count * 2 < lines.Count)
        {
            return null;
        }

        var decimalSeparator = matches.Any(m => m.Groups["weight"].Value.Contains(',', StringComparison.Ordinal)) ? "," : ".";
        var options = new GenericAsciiOptions { Pattern = ProposedPattern, DecimalSeparator = decimalSeparator };
        var parser = new GenericAsciiParser(options);
        var reading = parser.Parse(lines[^1], timestamp) ?? parser.Parse(matches[^1].Value, timestamp);
        if (reading is null)
        {
            return null;
        }

        var settings = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["pattern"] = ProposedPattern,
            ["decimalSeparator"] = decimalSeparator,
        };
        return new ProbeMatch("generic-ascii", "ASCII", settings, reading);
    }

    private static List<string> SplitLines(ReadOnlySpan<byte> received)
    {
        var text = Encoding.Latin1.GetString(received);
        var lines = new List<string>();
        foreach (var piece in text.Split(['\r', '\n']))
        {
            if (piece.Trim().Length > 0)
            {
                lines.Add(piece);
            }
        }

        // Without a terminator after it, the last piece may be cut off.
        if (lines.Count > 0 && received.Length > 0 && received[^1] is not ((byte)'\r' or (byte)'\n'))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static bool IsPrintable(string line) => line.All(c => c is '\t' or (>= ' ' and <= '~'));

    [GeneratedRegex(ProposedPattern, RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LinePattern();
}
