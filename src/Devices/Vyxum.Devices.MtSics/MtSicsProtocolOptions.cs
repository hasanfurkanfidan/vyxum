namespace Vyxum.Devices.MtSics;

/// <summary>Settings for <see cref="MtSicsProtocol"/>.</summary>
public sealed record MtSicsProtocolOptions
{
    /// <summary>The only commands the protocol may send. They read the weight and change nothing on the device.</summary>
    public static IReadOnlyList<string> AllowedCommands { get; } = ["S", "SI", "SIR"];

    /// <summary>
    /// The read command: <c>SI</c> (immediate value, default), <c>S</c> (stable value) or <c>SIR</c> (continuous stream,
    /// sent once). Any other command (tare, zero, reset, configuration) is rejected.
    /// </summary>
    public string Command
    {
        get;
        init
        {
            if (!AllowedCommands.Contains(value, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Command '{value}' is not allowed. Only read commands are sent: {string.Join(", ", AllowedCommands)}.",
                    nameof(value));
            }

            field = value;
        }
    } = "SI";

    /// <summary>The time between the start of two polls (S and SI). Default: 200 ms.</summary>
    public TimeSpan PollInterval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMilliseconds(200);

    /// <summary>How long to wait for a response line. Default: 1000 ms.</summary>
    public TimeSpan ResponseTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMilliseconds(1000);

    /// <summary>How many times a command is resent at once after <c>ES</c> or <c>ET</c>. Default: 2.</summary>
    public int RetriesOnError
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 2;

    /// <summary>After this many timeouts in a row the link is considered dead. Default: 3.</summary>
    public int MaxConsecutiveTimeouts
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 3;

    /// <summary>The terminator of response lines. Commands are always sent with CR LF. Default: CR LF.</summary>
    public LineTerminator LineTerminator { get; init; } = LineTerminator.CrLf;
}
