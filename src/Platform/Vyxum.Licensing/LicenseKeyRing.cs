using System.Security.Cryptography;

namespace Vyxum.Licensing;

/// <summary>Looks up the public key a license names in its <c>keyId</c>.</summary>
public interface ILicenseKeyRing
{
    /// <summary>Returns the SubjectPublicKeyInfo (DER) of <paramref name="keyId"/>, if the key is trusted.</summary>
    bool TryGetPublicKey(string keyId, out ReadOnlyMemory<byte> subjectPublicKeyInfo);
}

/// <summary>An immutable set of trusted ECDSA P-256 public keys by key id.</summary>
public sealed class LicenseKeyRing : ILicenseKeyRing
{
    private const string P256Oid = "1.2.840.10045.3.1.7";

    private readonly Dictionary<string, byte[]> _keys;

    /// <summary>Creates a key ring from key id → SubjectPublicKeyInfo (DER) pairs.</summary>
    /// <exception cref="ArgumentException">A key is not a valid ECDSA P-256 public key, or a key id is empty or repeated.</exception>
    public LicenseKeyRing(IEnumerable<KeyValuePair<string, byte[]>> publicKeys)
    {
        ArgumentNullException.ThrowIfNull(publicKeys);
        _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (keyId, spki) in publicKeys)
        {
            if (string.IsNullOrWhiteSpace(keyId))
                throw new ArgumentException("A key id must not be empty.", nameof(publicKeys));
            EnsureP256PublicKey(keyId, spki);
            if (!_keys.TryAdd(keyId, spki.ToArray()))
                throw new ArgumentException($"Key id '{keyId}' is listed twice.", nameof(publicKeys));
        }
    }

    /// <summary>The keys that production builds trust (<see cref="TrustedKeys"/>).</summary>
    public static LicenseKeyRing Trusted { get; } = FromBase64(TrustedKeys.PublicKeys);

    /// <summary>Creates a key ring from key id → SubjectPublicKeyInfo (base64 DER) pairs.</summary>
    public static LicenseKeyRing FromBase64(IEnumerable<KeyValuePair<string, string>> publicKeys)
    {
        ArgumentNullException.ThrowIfNull(publicKeys);
        return new LicenseKeyRing(publicKeys.Select(p => KeyValuePair.Create(p.Key, Convert.FromBase64String(p.Value))));
    }

    /// <summary>The trusted key ids.</summary>
    public IReadOnlyCollection<string> KeyIds => _keys.Keys;

    /// <inheritdoc />
    public bool TryGetPublicKey(string keyId, out ReadOnlyMemory<byte> subjectPublicKeyInfo)
    {
        if (_keys.TryGetValue(keyId, out var spki))
        {
            subjectPublicKeyInfo = spki;
            return true;
        }

        subjectPublicKeyInfo = default;
        return false;
    }

    private static void EnsureP256PublicKey(string keyId, byte[] spki)
    {
        ArgumentNullException.ThrowIfNull(spki);
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length || ecdsa.ExportParameters(false).Curve.Oid.Value != P256Oid)
                throw new ArgumentException($"Key '{keyId}' is not an ECDSA P-256 public key.", nameof(spki));
        }
        catch (CryptographicException ex)
        {
            throw new ArgumentException($"Key '{keyId}' is not an ECDSA P-256 public key.", nameof(spki), ex);
        }
    }

    internal static bool IsP256(ECDsa key) => key.ExportParameters(false).Curve.Oid.Value == P256Oid;
}
