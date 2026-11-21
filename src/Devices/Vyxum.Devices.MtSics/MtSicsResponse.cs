namespace Vyxum.Devices.MtSics;

/// <summary>The kind of an MT-SICS response line.</summary>
public enum MtSicsResponseKind
{
    /// <summary>The line could not be understood.</summary>
    Unknown,

    /// <summary>A weight value (<c>S S</c> stable or <c>S D</c> dynamic).</summary>
    Weight,

    /// <summary><c>S +</c>: the load is above the range.</summary>
    Overload,

    /// <summary><c>S -</c>: the load is below the range.</summary>
    Underload,

    /// <summary><c>S I</c>: the command was understood but cannot be executed now (busy).</summary>
    Busy,

    /// <summary><c>ES</c>: syntax error, the command was not recognised.</summary>
    SyntaxError,

    /// <summary><c>ET</c>: transmission error.</summary>
    TransmissionError,

    /// <summary><c>EL</c>: logical error, the command cannot be executed.</summary>
    LogicalError,

    /// <summary><c>I4 A "..."</c>: the serial number.</summary>
    SerialNumber,

    /// <summary><c>Z A</c>: the zero command was executed.</summary>
    ZeroDone,

    /// <summary><c>Z I</c>: the zero command cannot be executed now (for example the weight is in motion).</summary>
    ZeroNotExecutable,

    /// <summary><c>Z +</c>: the weight is above the zero setting range.</summary>
    ZeroUpperLimit,

    /// <summary><c>Z -</c>: the weight is below the zero setting range.</summary>
    ZeroLowerLimit,
}

/// <summary>One parsed MT-SICS response line.</summary>
/// <param name="Kind">What the line is.</param>
/// <param name="Raw">The line as received.</param>
/// <param name="Value">For <see cref="MtSicsResponseKind.Weight"/>, the value exactly as transmitted (scale kept).</param>
/// <param name="Unit">For <see cref="MtSicsResponseKind.Weight"/>, the unit as transmitted; null when missing.</param>
/// <param name="IsStable">For <see cref="MtSicsResponseKind.Weight"/>, true for <c>S S</c> and false for <c>S D</c>.</param>
/// <param name="SerialNumber">For <see cref="MtSicsResponseKind.SerialNumber"/>, the serial number without quotes.</param>
public sealed record MtSicsResponse(
    MtSicsResponseKind Kind,
    string Raw,
    decimal? Value = null,
    string? Unit = null,
    bool IsStable = false,
    string? SerialNumber = null);
