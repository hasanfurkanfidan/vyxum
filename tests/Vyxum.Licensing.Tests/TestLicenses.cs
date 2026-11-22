using System.Security.Cryptography;

namespace Vyxum.Licensing.Tests;

/// <summary>Ephemeral keys and sample payloads. Keys are generated at test runtime and never stored.</summary>
internal sealed class TestKey : IDisposable
{
    public TestKey(string keyId)
    {
        KeyId = keyId;
        Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Signer = new LicenseSigner(Key, keyId);
    }

    public string KeyId { get; }

    public ECDsa Key { get; }

    public LicenseSigner Signer { get; }

    public byte[] PublicKey => Key.ExportSubjectPublicKeyInfo();

    public LicenseKeyRing Ring() => new([KeyValuePair.Create(KeyId, PublicKey)]);

    public LicenseReader Reader() => new(Ring());

    public void Dispose() => Key.Dispose();
}

internal static class TestLicenses
{
    public static LicensePayload Subscription(
        DateOnly validFrom, DateOnly validUntil, int graceDays = LicensePayload.DefaultGraceDays, params string[] modules) => new()
    {
        LicenseId = "VX-2026-0001",
        Type = LicenseType.Subscription,
        Licensee = new Licensee { Name = "Örnek Gıda A.Ş.", TaxNumber = "1234567890" },
        Modules = modules.Length == 0 ? ["labels"] : modules,
        Stations = 2,
        IssuedAt = new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.FromHours(3)),
        ValidFrom = validFrom,
        ValidUntil = validUntil,
        GraceDays = graceDays,
    };

    public static LicensePayload Sample() => Subscription(new DateOnly(2026, 11, 1), new DateOnly(2027, 10, 31));
}

/// <summary>A clock fixed at a given instant, with a configurable local time zone.</summary>
internal sealed class FixedClock(DateTimeOffset utcNow, TimeZoneInfo localTimeZone) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public override TimeZoneInfo LocalTimeZone => localTimeZone;
}
