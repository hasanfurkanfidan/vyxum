using System.Resources;

namespace Vyxum.Platform;

/// <summary>
/// Resources of the glossary screen: the labels of the screen (<c>Glossary/Resources/Strings.resx</c>, served under the
/// <see cref="KeyPrefix"/> prefix in <c>GET /api/ui/strings</c>) and the glossary itself
/// (<c>Glossary/Resources/Terms.resx</c>, served by <c>GET /api/glossary</c>).
/// </summary>
public static class GlossaryStrings
{
    /// <summary>Prefix of the glossary screen strings in <c>GET /api/ui/strings</c>.</summary>
    public const string KeyPrefix = "glossary";

    /// <summary>Resource manager of the glossary screen strings.</summary>
    public static ResourceManager ResourceManager { get; } =
        new("Vyxum.Platform.Glossary.Resources.Strings", typeof(GlossaryStrings).Assembly);

    /// <summary>
    /// Resource manager of the glossary content. Keys: <c>S{section}.Title</c>, <c>S{section}.Intro</c> and
    /// <c>T{section}.{number}.Name</c>, <c>.Text</c>, optional <c>.InVyxum</c> and <c>.Example</c>.
    /// </summary>
    public static ResourceManager Terms { get; } =
        new("Vyxum.Platform.Glossary.Resources.Terms", typeof(GlossaryStrings).Assembly);
}
