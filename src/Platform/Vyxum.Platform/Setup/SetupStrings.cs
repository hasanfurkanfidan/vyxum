using System.Resources;

namespace Vyxum.Platform;

/// <summary>
/// UI strings of the setup wizard (<c>Setup/Resources/Strings.resx</c> and <c>Strings.tr.resx</c>), served under the
/// <see cref="KeyPrefix"/> prefix. Error codes of the setup API are keys of this table (<c>setup.DbLoginFailed</c>).
/// </summary>
public static class SetupStrings
{
    /// <summary>Prefix of the setup strings in <c>GET /api/ui/strings</c>.</summary>
    public const string KeyPrefix = "setup";

    /// <summary>Resource manager of the setup strings.</summary>
    public static ResourceManager ResourceManager { get; } =
        new("Vyxum.Platform.Setup.Resources.Strings", typeof(SetupStrings).Assembly);

    /// <summary>The full UI key of <paramref name="key"/>, for example <c>setup.DbLoginFailed</c>.</summary>
    internal static string Code(string key) => KeyPrefix + "." + key;
}
