using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vyxum.Licensing;

/// <summary>
/// Reads and verifies <c>.vxlic</c> files. Never throws for bad input; every problem is reported as a
/// <see cref="LicenseReadResult"/> with an error code.
/// </summary>
/// <remarks>
/// <para>
/// Checks, in order: size (at most <see cref="MaxLength"/> characters), JSON envelope (exactly <c>format</c>,
/// <c>keyId</c>, <c>payload</c> and <c>signature</c>; any other property is rejected), <c>format</c> string, standard
/// base64 without white space, trusted <c>keyId</c>, ECDSA P-256 / SHA-256 signature (IEEE P1363) over the exact decoded
/// payload bytes, and finally the payload fields. The payload is parsed only after its signature is verified. Unknown
/// payload fields are ignored (an older version reads a newer file); they are signed, so they cannot be added later.
/// </para>
/// <para>
/// The verified license keeps a canonical text (<see cref="VerifiedLicense.RawText"/>), not the text as read: the
/// envelope is written again from the verified values, with the signature in low-S form (see
/// <see cref="EcdsaSignatures"/>). Nothing outside the signature reaches storage or paid modules.
/// </para>
/// </remarks>
public sealed class LicenseReader(ILicenseKeyRing keyRing)
{
    /// <summary>The largest accepted file, in characters (the limit of a <c>plt_Settings</c> value).</summary>
    public const int MaxLength = LicenseJson.MaxLength;

    /// <summary>The format string this build reads.</summary>
    public const string Format = LicenseJson.FormatV1;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ILicenseKeyRing _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));

    /// <summary>Reads a license file from its UTF-8 bytes (a leading byte order mark is accepted).</summary>
    public LicenseReadResult Read(ReadOnlySpan<byte> utf8)
    {
        // A UTF-8 character is at most 4 bytes, so anything longer cannot fit in the character limit.
        if (utf8.Length > MaxLength * 4)
            return TooLarge();
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            utf8 = utf8[3..];

        string text;
        try
        {
            text = StrictUtf8.GetString(utf8);
        }
        catch (DecoderFallbackException)
        {
            return LicenseReadResult.Failure(LicenseReadError.InvalidFormat, "The file is not valid UTF-8.");
        }

        return Read(text);
    }

    /// <summary>Reads a license file from its text.</summary>
    public LicenseReadResult Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return LicenseReadResult.Failure(LicenseReadError.InvalidFormat, "The file is empty.");
        if (text.Length > MaxLength)
            return TooLarge();

        LicenseJson.Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<LicenseJson.Envelope>(text, LicenseJson.Options);
        }
        catch (JsonException ex)
        {
            return LicenseReadResult.Failure(LicenseReadError.InvalidFormat, $"The file is not a license envelope: {ex.Message}");
        }

        if (envelope is null || envelope.Format is null || envelope.KeyId is null || envelope.Payload is null || envelope.Signature is null)
            return LicenseReadResult.Failure(LicenseReadError.InvalidFormat, "The envelope must contain format, keyId, payload and signature.");
        if (!string.Equals(envelope.Format, Format, StringComparison.Ordinal))
            return LicenseReadResult.Failure(LicenseReadError.UnsupportedFormat, $"Format '{envelope.Format}' is not supported; expected '{Format}'.");

        if (DecodeCanonicalBase64(envelope.Payload) is not { } payloadBytes || DecodeCanonicalBase64(envelope.Signature) is not { } signature)
            return LicenseReadResult.Failure(LicenseReadError.InvalidFormat, "payload and signature must be standard base64 without white space.");

        if (!_keyRing.TryGetPublicKey(envelope.KeyId, out var publicKey))
            return LicenseReadResult.Failure(LicenseReadError.UnknownKey, $"Key '{envelope.KeyId}' is not trusted.");
        if (!VerifySignature(publicKey.Span, payloadBytes, signature))
            return LicenseReadResult.Failure(LicenseReadError.InvalidSignature, "The signature does not match the payload.");

        LicenseJson.RawPayload? raw;
        try
        {
            raw = JsonSerializer.Deserialize<LicenseJson.RawPayload>(payloadBytes, LicenseJson.Options);
        }
        catch (JsonException ex)
        {
            return LicenseReadResult.Failure(LicenseReadError.InvalidPayload, $"The payload is not valid: {ex.Message}");
        }

        if (raw is null)
            return LicenseReadResult.Failure(LicenseReadError.InvalidPayload, "The payload is empty.");
        var payload = LicensePayloadValidator.FromRaw(raw, out var problem);
        if (payload is null)
            return LicenseReadResult.Failure(LicenseReadError.InvalidPayload, problem!);

        // Only signed content is kept: the envelope is written again from the verified values (see LicenseJson.WriteEnvelope).
        EcdsaSignatures.NormalizeLowS(signature);
        var canonical = LicenseJson.WriteEnvelope(envelope.KeyId, payloadBytes, signature);
        return LicenseReadResult.Success(new VerifiedLicense(envelope.KeyId, payload, canonical));
    }

    /// <summary>The bytes of <paramref name="text"/>, or null unless it is exactly the standard base64 of those bytes.</summary>
    private static byte[]? DecodeCanonicalBase64(string text)
    {
        try
        {
            var bytes = Convert.FromBase64String(text);
            return string.Equals(Convert.ToBase64String(bytes), text, StringComparison.Ordinal) ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool VerifySignature(ReadOnlySpan<byte> subjectPublicKeyInfo, byte[] data, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static LicenseReadResult TooLarge() =>
        LicenseReadResult.Failure(LicenseReadError.TooLarge, $"The file is longer than {MaxLength} characters.");
}
