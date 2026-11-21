using System.Resources;

namespace Vyxum.Desktop;

/// <summary>Localized texts of the window (<c>Resources/Strings.resx</c> English, <c>Strings.tr.resx</c> Turkish).</summary>
internal static class Strings
{
    private static readonly ResourceManager Manager = new("Vyxum.Desktop.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string name) => Manager.GetString(name) ?? name;

    public static string Get(string name, params object[] args) => string.Format(Get(name), args);
}
