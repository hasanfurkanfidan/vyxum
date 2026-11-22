namespace Vyxum.Devices.GenericAscii;

/// <summary>
/// Settings for <see cref="GenericAsciiProtocol"/>: rules that describe a line-based ASCII output, so that a scale
/// can be read without brand-specific code. All properties are plain values and can be bound from configuration.
/// They are checked when a <see cref="GenericAsciiParser"/> or <see cref="GenericAsciiProtocol"/> is created.
/// </summary>
public sealed record GenericAsciiOptions
{
    /// <summary>
    /// Required. A .NET regular expression matched against each line. Named groups: <c>weight</c> (required; may contain a
    /// sign, spaces and the decimal and thousands separators), and optionally <c>unit</c>, <c>sign</c> and <c>status</c>.
    /// Lines that do not match are skipped.
    /// </summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary>The decimal separator in the <c>weight</c> group: "." (default) or ",".</summary>
    public string DecimalSeparator { get; init; } = ".";

    /// <summary>An optional thousands separator, removed from the <c>weight</c> group before parsing. Must differ from the decimal separator.</summary>
    public string? ThousandsSeparator { get; init; }

    /// <summary>
    /// Optional regex that marks a stable weight. Tested against the <c>status</c> group when the pattern has one and it
    /// matched, otherwise against the whole line.
    /// </summary>
    public string? StablePattern { get; init; }

    /// <summary>Optional regex that marks a weight in motion. Tested like <see cref="StablePattern"/>.</summary>
    public string? MotionPattern { get; init; }

    /// <summary>Optional regex that marks an overload. Tested like <see cref="StablePattern"/>; the reading then has no value.</summary>
    public string? OverloadPattern { get; init; }

    /// <summary>Optional regex that marks an underload. Tested like <see cref="StablePattern"/>; the reading then has no value.</summary>
    public string? UnderloadPattern { get; init; }

    /// <summary>Optional regex that marks a net weight. When set, readings report net (match) or gross (no match); otherwise net is unknown.</summary>
    public string? NetPattern { get; init; }

    /// <summary>The unit used when the line has none (no <c>unit</c> group, or it is empty). Default: none.</summary>
    public string? DefaultUnit { get; init; }

    /// <summary>The line terminator of the device output. Default: any of CR, LF or CR LF.</summary>
    public LineTerminator Terminator { get; init; } = LineTerminator.Any;

    /// <summary>
    /// The commands allowed as <see cref="RequestCommand"/>: read or print commands of common brands that change nothing
    /// on the device. <c>P</c> (print), <c>W</c> (weight), <c>S</c> (stable weight), <c>SI</c> (weight now), <c>Q</c>
    /// (output once), <c>IP</c> (print now), <c>READ</c>, ENQ (<c>\u0005</c>) and ESC P (<c>\u001bP</c>). Tare, zero,
    /// reset and setting commands can never be configured. The web application checks the same list.
    /// </summary>
    public static IReadOnlyList<string> AllowedRequestCommands { get; } = ["P", "W", "S", "SI", "Q", "IP", "READ", "\u0005", "\u001bP"];

    /// <summary>
    /// Optional demand mode: the command sent before each read, without its terminator. It must be exactly one of
    /// <see cref="AllowedRequestCommands"/> (case-sensitive), so nothing that tares, zeroes or changes the device is
    /// ever sent. Empty means listen-only mode: the device sends on its own.
    /// </summary>
    public string? RequestCommand { get; init; }

    /// <summary>The terminator appended to <see cref="RequestCommand"/>. Only CR and LF characters are allowed. Default: CR LF.</summary>
    public string RequestTerminator { get; init; } = "\r\n";

    /// <summary>In demand mode, the time between the start of two requests. Default: 200 ms.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>In demand mode, how long to wait for a matching response line. Default: 1000 ms.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>In demand mode, after this many requests in a row without a matching response the link is considered dead. Default: 3.</summary>
    public int MaxConsecutiveTimeouts { get; init; } = 3;

    /// <summary>In listen-only mode, when no line matches the pattern for this long the link is considered dead. Default: 3 s.</summary>
    public TimeSpan InactivityTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The longest line accepted; longer lines are dropped. Default: 256.</summary>
    public int MaxLineLength { get; init; } = 256;

    /// <summary>True when <see cref="RequestCommand"/> is set, so the protocol polls instead of listening.</summary>
    public bool IsDemandMode => !string.IsNullOrEmpty(RequestCommand);

    /// <summary>Throws <see cref="ArgumentException"/> describing the first invalid setting.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Pattern))
        {
            throw new ArgumentException("Pattern is required.", nameof(Pattern));
        }

        if (DecimalSeparator is not ("." or ","))
        {
            throw new ArgumentException("DecimalSeparator must be \".\" or \",\".", nameof(DecimalSeparator));
        }

        if (ThousandsSeparator is not null && (ThousandsSeparator.Length == 0 || ThousandsSeparator == DecimalSeparator || ThousandsSeparator.Any(char.IsDigit)))
        {
            throw new ArgumentException("ThousandsSeparator must be non-empty, contain no digit and differ from DecimalSeparator.", nameof(ThousandsSeparator));
        }

        if (!string.IsNullOrEmpty(RequestCommand) && !AllowedRequestCommands.Contains(RequestCommand, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "RequestCommand must be one of the read commands: P, W, S, SI, Q, IP, READ, ENQ (\\u0005) or ESC P (\\u001bP).",
                nameof(RequestCommand));
        }

        if (RequestTerminator.Length == 0 || RequestTerminator.Any(c => c is not ('\r' or '\n')))
        {
            throw new ArgumentException("RequestTerminator must consist of CR and LF characters only.", nameof(RequestTerminator));
        }

        if (!Enum.IsDefined(Terminator))
        {
            throw new ArgumentException("Terminator is not valid.", nameof(Terminator));
        }

        RequirePositive(PollInterval, nameof(PollInterval));
        RequirePositive(ResponseTimeout, nameof(ResponseTimeout));
        RequirePositive(InactivityTimeout, nameof(InactivityTimeout));
        if (MaxConsecutiveTimeouts < 1)
        {
            throw new ArgumentException("MaxConsecutiveTimeouts must be at least 1.", nameof(MaxConsecutiveTimeouts));
        }

        if (MaxLineLength < 1)
        {
            throw new ArgumentException("MaxLineLength must be at least 1.", nameof(MaxLineLength));
        }
    }

    private static void RequirePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentException($"{name} must be greater than zero.", name);
        }
    }
}
