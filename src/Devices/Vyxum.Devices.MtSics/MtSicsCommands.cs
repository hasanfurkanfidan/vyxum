namespace Vyxum.Devices.MtSics;

/// <summary>
/// The guard for everything written to an MT-SICS device. Only the read commands and, on an explicit user request, the
/// zero command <c>Z</c> may be sent (decisions V-28 and V-29). Tare (<c>T</c>, <c>TI</c>, <c>TA</c>), reset (<c>@</c>),
/// immediate zero (<c>ZI</c>) and every settings command are never sent.
/// </summary>
public static class MtSicsCommands
{
    /// <summary>The zero command. Sent once per <see cref="MtSicsProtocol.ZeroAsync"/> call.</summary>
    public const string Zero = "Z";

    /// <summary>The read commands. They read the weight and change nothing on the device.</summary>
    public static IReadOnlyList<string> Read { get; } = ["S", "SI", "SIR"];

    /// <summary>Every command the protocol may write: the read commands and <see cref="Zero"/>.</summary>
    public static IReadOnlyList<string> Sendable { get; } = [.. Read, Zero];

    /// <summary>True when <paramref name="command"/> is exactly one of <see cref="Sendable"/> (case-sensitive, no extra text).</summary>
    public static bool IsSendable(string? command) =>
        command is not null && Sendable.Contains(command, StringComparer.Ordinal);

    /// <summary>Throws <see cref="InvalidOperationException"/> unless <paramref name="command"/> may be sent.</summary>
    public static void EnsureSendable(string? command)
    {
        if (!IsSendable(command))
        {
            throw new InvalidOperationException(
                $"The MT-SICS command '{command}' must never be sent. Only these are allowed: {string.Join(", ", Sendable)}.");
        }
    }
}
