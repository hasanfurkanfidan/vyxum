using System.Resources;

namespace Vyxum.Host;

/// <summary>Marker type for the host's localized strings (<c>Resources/Strings.resx</c>, <c>Strings.tr.resx</c>).</summary>
public sealed class Strings
{
    /// <summary>Prefix of the host's strings in <c>GET /api/ui/strings</c>.</summary>
    public const string UiPrefix = "shell";

    private Strings()
    {
    }

    /// <summary>The resource manager of <c>Resources/Strings.resx</c>.</summary>
    public static ResourceManager ResourceManager { get; } =
        new("Vyxum.Host.Resources.Strings", typeof(Strings).Assembly);
}
