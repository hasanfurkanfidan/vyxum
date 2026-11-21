using System.Globalization;
using System.Resources;

namespace Vyxum.Modules.Weighing;

/// <summary>The module's UI strings (<c>Resources/Strings.resx</c> and <c>Strings.tr.resx</c>). Keys are listed without a module prefix.</summary>
public static class WeighingStrings
{
    /// <summary>Prefix the UI puts in front of every key of this module.</summary>
    public const string KeyPrefix = "weighing";

    /// <summary>Resource manager of the module's strings.</summary>
    public static ResourceManager ResourceManager { get; } =
        new("Vyxum.Modules.Weighing.Resources.Strings", typeof(WeighingStrings).Assembly);

    /// <summary>Returns the string for <paramref name="key"/> in <paramref name="culture"/>, or null when the key is unknown.</summary>
    public static string? Get(string key, CultureInfo? culture = null) => ResourceManager.GetString(key, culture);
}
