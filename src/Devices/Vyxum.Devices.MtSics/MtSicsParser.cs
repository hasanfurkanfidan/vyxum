using System.Globalization;

namespace Vyxum.Devices.MtSics;

/// <summary>Parses MT-SICS response lines. Never throws; lines it does not understand become <see cref="MtSicsResponseKind.Unknown"/>.</summary>
public static class MtSicsParser
{
    private const NumberStyles ValueStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>Parses one line without its terminator.</summary>
    public static MtSicsResponse Parse(string? line)
    {
        var raw = line ?? string.Empty;
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Unknown(raw);
        }

        return tokens[0] switch
        {
            "S" => ParseWeight(raw, tokens),
            "ES" when tokens.Length == 1 => new MtSicsResponse(MtSicsResponseKind.SyntaxError, raw),
            "ET" when tokens.Length == 1 => new MtSicsResponse(MtSicsResponseKind.TransmissionError, raw),
            "EL" when tokens.Length == 1 => new MtSicsResponse(MtSicsResponseKind.LogicalError, raw),
            "I4" => ParseSerialNumber(raw, tokens),
            "Z" when tokens.Length == 2 => ParseZero(raw, tokens[1]),
            _ => Unknown(raw),
        };
    }

    private static MtSicsResponse ParseWeight(string raw, string[] tokens)
    {
        if (tokens.Length == 2)
        {
            return tokens[1] switch
            {
                "+" => new MtSicsResponse(MtSicsResponseKind.Overload, raw),
                "-" => new MtSicsResponse(MtSicsResponseKind.Underload, raw),
                "I" => new MtSicsResponse(MtSicsResponseKind.Busy, raw),
                _ => Unknown(raw),
            };
        }

        if (tokens.Length is < 3 or > 4 || tokens[1] is not ("S" or "D"))
        {
            return Unknown(raw);
        }

        if (!decimal.TryParse(tokens[2], ValueStyle, CultureInfo.InvariantCulture, out var value))
        {
            return Unknown(raw);
        }

        var unit = tokens.Length == 4 ? tokens[3] : null;
        return new MtSicsResponse(MtSicsResponseKind.Weight, raw, value, unit, IsStable: tokens[1] == "S");
    }

    private static MtSicsResponse ParseZero(string raw, string status) => status switch
    {
        "A" => new MtSicsResponse(MtSicsResponseKind.ZeroDone, raw),
        "I" => new MtSicsResponse(MtSicsResponseKind.ZeroNotExecutable, raw),
        "+" => new MtSicsResponse(MtSicsResponseKind.ZeroUpperLimit, raw),
        "-" => new MtSicsResponse(MtSicsResponseKind.ZeroLowerLimit, raw),
        _ => Unknown(raw),
    };

    private static MtSicsResponse ParseSerialNumber(string raw, string[] tokens)
    {
        if (tokens.Length < 3 || tokens[1] != "A")
        {
            return Unknown(raw);
        }

        var rest = raw.Trim();
        rest = rest[(rest.IndexOf('A', StringComparison.Ordinal) + 1)..].Trim();
        if (rest.Length < 2 || rest[0] != '"' || rest[^1] != '"')
        {
            return Unknown(raw);
        }

        return new MtSicsResponse(MtSicsResponseKind.SerialNumber, raw, SerialNumber: rest[1..^1]);
    }

    private static MtSicsResponse Unknown(string raw) => new(MtSicsResponseKind.Unknown, raw);
}
