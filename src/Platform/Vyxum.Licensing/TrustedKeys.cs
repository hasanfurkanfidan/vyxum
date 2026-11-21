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
        // Development/trial key. Replace it before the first real customer license and remove it from this list
        // (decision V-51 in the planning docs).
        ["vx-2026-1"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEbIXgp+ZHgbHphPw5g+/Cyy3xIJJsA9gYWEKPyAUFLpnIR/K1Uax6vquG6mOyzucK/qt80yfV2pxwMi2XMirY7Q==",

        // Only public keys belong in this file. Never add a private key, here or anywhere in this repository.
    };
}
