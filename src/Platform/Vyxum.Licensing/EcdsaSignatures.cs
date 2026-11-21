using System.Numerics;

namespace Vyxum.Licensing;

/// <summary>
/// Low-S form of ECDSA P-256 signatures (IEEE P1363: <c>r || s</c>, 32 bytes each).
/// </summary>
/// <remarks>
/// For every valid signature <c>(r, s)</c>, <c>(r, n - s)</c> is valid too, so a signed file can be changed without the
/// key. The reader accepts both (files issued before this rule may carry either) and keeps only the low-S form
/// (<c>s ≤ n / 2</c>); the signer always writes it. So the stored text of a license does not depend on how the file
/// was shaped.
/// </remarks>
internal static class EcdsaSignatures
{
    /// <summary>The length of a P-256 signature in IEEE P1363 form.</summary>
    public const int P256Length = 64;

    private const int ScalarLength = P256Length / 2;

    // The order n of the P-256 group.
    private static readonly BigInteger Order = new(
        Convert.FromHexString("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"),
        isUnsigned: true,
        isBigEndian: true);

    private static readonly BigInteger HalfOrder = Order >> 1;

    /// <summary>Replaces <c>s</c> with <c>n - s</c> in place when <c>s</c> is above <c>n / 2</c>; returns whether it changed.</summary>
    public static bool NormalizeLowS(Span<byte> signature)
    {
        if (signature.Length != P256Length)
            return false;
        var s = new BigInteger(signature[ScalarLength..], isUnsigned: true, isBigEndian: true);
        if (s <= HalfOrder)
            return false;

        var low = Order - s;
        var target = signature[ScalarLength..];
        target.Clear();
        var length = low.GetByteCount(isUnsigned: true);
        low.TryWriteBytes(target[(ScalarLength - length)..], out _, isUnsigned: true, isBigEndian: true);
        return true;
    }
}
