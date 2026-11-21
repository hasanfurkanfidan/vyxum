namespace Vyxum.Platform.Abstractions;

/// <summary>
/// What a module that requires a license shows while it is locked: a short description and a static sample image of its
/// screen, so the operator sees what is available. The shell shows the image blurred, in a "Vyxum Pro" menu group.
/// </summary>
/// <remarks>
/// Contract for module authors (the paid modules in <c>vyxum-pro</c> follow it):
/// <list type="bullet">
/// <item>The preview is optional; a locked module without one only gets the plain "license required" screen.</item>
/// <item><see cref="DescriptionKey"/> is a full key (prefix included) in a string set the module registers itself
/// (<c>AddUiStrings</c>), with Turkish and English text: two short lines saying what the module does. The platform
/// checks at startup that the key exists.</item>
/// <item><see cref="Image"/> is an embedded resource under <see cref="ResourcePrefix"/>, for example
/// <c>&lt;EmbeddedResource Include="preview\**\*" LogicalName="vyxum-preview/%(RecursiveDir)%(Filename)%(Extension)" /&gt;</c>:
/// a hand-made static sample (SVG, PNG or WebP) with made-up data, never a real screen or the module's code. The
/// platform serves it at <c>/modules/{moduleId}/preview/{file}</c> whatever the license state; files under
/// <c>vyxum-ui/</c> are never reachable through that address.</item>
/// <item>No address, price or other business information belongs in the preview; the contact line comes from
/// configuration (<c>Vyxum:Contact:Email</c>, <c>Vyxum:Contact:Url</c>).</item>
/// </list>
/// </remarks>
public sealed class ModulePreview
{
    /// <summary>Logical name prefix of the embedded preview files.</summary>
    public const string ResourcePrefix = "vyxum-preview/";

    /// <summary>Full UI string key of the two-line description, for example <c>etiket.PreviewDescription</c>.</summary>
    public required string DescriptionKey { get; init; }

    /// <summary>Path of the sample image relative to <see cref="ResourcePrefix"/>, for example <c>screen.svg</c>.</summary>
    public required string Image { get; init; }
}
