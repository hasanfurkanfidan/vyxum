using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vyxum.Licensing;

/// <summary>
/// Produces signed <c>.vxlic</c> files. Only the license tool and tests use it; the product only verifies.
/// </summary>
public sealed class LicenseSigner
{
    private readonly ECDsa _privateKey;
    private readonly string _keyId;

    /// <summary>Creates a signer for <paramref name="privateKey"/> (ECDSA P-256), published under <paramref name="keyId"/>.</summary>
    /// <remarks>The signer does not own the key; the caller disposes it.</remarks>
    public LicenseSigner(ECDsa privateKey, string keyId)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (!LicenseKeyRing.IsP256(privateKey))
            throw new ArgumentException("The signing key must be an ECDSA P-256 key.", nameof(privateKey));
        _privateKey = privateKey;
        _keyId = keyId;
    }

    /// <summary>Validates <paramref name="payload"/>, serializes it as camelCase JSON and returns the signed file text.</summary>
    /// <exception cref="ArgumentException">The payload is invalid, or the file would exceed the size limit.</exception>
    public string Sign(LicensePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (LicensePayloadValidator.Validate(payload) is { } problem)
            throw new ArgumentException($"Invalid license payload: {problem}", nameof(payload));
        return SignPayloadBytes(JsonSerializer.SerializeToUtf8Bytes(payload, LicenseJson.Options));
    }

    /// <summary>
    /// Signs the given payload bytes as they are, without validating them, and returns the file text.
    /// Meant for tests and for files written by a newer tool version.
    /// </summary>
    /// <exception cref="ArgumentException">The file would exceed the size limit.</exception>
    public string SignPayloadBytes(ReadOnlySpan<byte> payloadUtf8)
    {
        var signature = _privateKey.SignData(payloadUtf8, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var text = JsonSerializer.Serialize(
            new LicenseJson.Envelope
            {
                Format = LicenseJson.FormatV1,
                KeyId = _keyId,
                Payload = Convert.ToBase64String(payloadUtf8),
                Signature = Convert.ToBase64String(signature),
            },
            LicenseJson.Options);
        if (text.Length > LicenseJson.MaxLength)
            throw new ArgumentException($"The license file would be {text.Length} characters; the limit is {LicenseJson.MaxLength}.");
        return text;
    }

    /// <summary>Same as <see cref="Sign(LicensePayload)"/>, as UTF-8 bytes (without a byte order mark) for writing to a file.</summary>
    public byte[] SignToUtf8(LicensePayload payload) => Encoding.UTF8.GetBytes(Sign(payload));
}
