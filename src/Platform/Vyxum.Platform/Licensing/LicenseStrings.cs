using System.Resources;

namespace Vyxum.Platform;

/// <summary>
/// UI strings of the license screens (<c>Licensing/Resources/Strings.resx</c> and <c>Strings.tr.resx</c>), served under
/// the <see cref="KeyPrefix"/> prefix. The messages for the error codes of <c>/api/license</c> are the keys
/// <c>license.Error.{code}</c> (for example <c>license.Error.invalid_signature</c>).
/// </summary>
public static class LicenseStrings
{
    /// <summary>Prefix of the license strings in <c>GET /api/ui/strings</c>.</summary>
    public const string KeyPrefix = "license";

    /// <summary>Resource manager of the license strings.</summary>
    public static ResourceManager ResourceManager { get; } =
        new("Vyxum.Platform.Licensing.Resources.Strings", typeof(LicenseStrings).Assembly);
}
