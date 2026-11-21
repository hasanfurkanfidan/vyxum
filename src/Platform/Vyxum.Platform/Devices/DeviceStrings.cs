using System.Resources;

namespace Vyxum.Platform;

/// <summary>
/// UI strings of the device settings (<c>Devices/Resources/Strings.resx</c> and <c>Strings.tr.resx</c>), served under
/// the <see cref="KeyPrefix"/> prefix. Validation errors of <c>/api/devices/config</c> are keys of this table
/// (<c>devices.IdTaken</c>).
/// </summary>
public static class DeviceStrings
{
    /// <summary>Prefix of the device strings in <c>GET /api/ui/strings</c>.</summary>
    public const string KeyPrefix = "devices";

    /// <summary>Resource manager of the device strings.</summary>
    public static ResourceManager ResourceManager { get; } =
        new("Vyxum.Platform.Devices.Resources.Strings", typeof(DeviceStrings).Assembly);

    /// <summary>The full UI key of <paramref name="key"/>, for example <c>devices.IdTaken</c>.</summary>
    internal static string Code(string key) => KeyPrefix + "." + key;
}
