namespace Vyxum.Licensing;

/// <summary>Why a license file was rejected.</summary>
public enum LicenseReadError
{
    /// <summary><c>invalid_format</c>: not JSON, not an envelope, missing fields or bad base64.</summary>
    InvalidFormat,

    /// <summary><c>unsupported_format</c>: the <c>format</c> field names a version this build does not read.</summary>
    UnsupportedFormat,

    /// <summary><c>unknown_key</c>: the file is signed with a key this build does not trust.</summary>
    UnknownKey,

    /// <summary><c>invalid_signature</c>: the signature does not match the payload (changed or forged file).</summary>
    InvalidSignature,

    /// <summary><c>invalid_payload</c>: the signature is valid but the payload is not a valid license.</summary>
    InvalidPayload,

    /// <summary><c>too_large</c>: the file is longer than the accepted limit.</summary>
    TooLarge,
}

/// <summary>A license file whose signature has been verified against a trusted key.</summary>
public sealed class VerifiedLicense
{
    internal VerifiedLicense(string keyId, LicensePayload payload, string rawText)
    {
        KeyId = keyId;
        Payload = payload;
        RawText = rawText;
    }

    /// <summary>The key id the file was signed with.</summary>
    public string KeyId { get; }

    /// <summary>The verified license content.</summary>
    public LicensePayload Payload { get; }

    /// <summary>The file text as read, for storing it and for re-verification by paid modules.</summary>
    public string RawText { get; }
}

/// <summary>The outcome of reading a license file: either a verified license or an error code.</summary>
public sealed class LicenseReadResult
{
    private LicenseReadResult(VerifiedLicense? license, LicenseReadError? error, string? detail)
    {
        License = license;
        Error = error;
        Detail = detail;
    }

    /// <summary>The verified license, when <see cref="IsValid"/>.</summary>
    public VerifiedLicense? License { get; }

    /// <summary>The reason for rejection, when not <see cref="IsValid"/>.</summary>
    public LicenseReadError? Error { get; }

    /// <summary>An English description of the problem, for logs. Not shown to users.</summary>
    public string? Detail { get; }

    /// <summary>Whether the file is a valid license signed with a trusted key.</summary>
    public bool IsValid => License is not null;

    /// <summary>The machine-readable error code (for example <c>invalid_signature</c>), or <see langword="null"/> when valid.</summary>
    public string? ErrorCode => Error switch
    {
        null => null,
        LicenseReadError.InvalidFormat => "invalid_format",
        LicenseReadError.UnsupportedFormat => "unsupported_format",
        LicenseReadError.UnknownKey => "unknown_key",
        LicenseReadError.InvalidSignature => "invalid_signature",
        LicenseReadError.InvalidPayload => "invalid_payload",
        LicenseReadError.TooLarge => "too_large",
        _ => throw new InvalidOperationException($"Unknown error {Error}."),
    };

    internal static LicenseReadResult Success(VerifiedLicense license) => new(license, null, null);

    internal static LicenseReadResult Failure(LicenseReadError error, string detail) => new(null, error, detail);
}
