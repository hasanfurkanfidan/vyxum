namespace Vyxum.Licensing;

/// <summary>
/// The public keys that production builds trust, as key id → SubjectPublicKeyInfo (base64 DER, ECDSA P-256).
/// The list is compiled in on purpose: it cannot be changed from a settings file. When the signing key changes,
/// a new entry is added and old entries stay, so files signed with an older key remain valid.
/// </summary>
public static class TrustedKeys
{
    /// <summary>The trusted public keys by key id.</summary>
    public static IReadOnlyDictionary<string, string> PublicKeys { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Placeholder: the production public key is added here when it is generated (phase F4, step 4.7).
        // Only public keys belong in this file. Never add a private key, here or anywhere in this repository.
    };
}
