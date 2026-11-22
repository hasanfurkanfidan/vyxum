namespace Vyxum.Platform.Abstractions;

/// <summary>
/// The screens a module brings with it: a compiled ES module (and its other files) embedded in the module's assembly.
/// </summary>
/// <remarks>
/// Every file of the module's front end is an embedded resource whose logical name starts with
/// <see cref="ResourcePrefix"/>, e.g. <c>vyxum-ui/index.js</c>; in the project file:
/// <code>&lt;EmbeddedResource Include="ui\**\*" LogicalName="vyxum-ui/%(RecursiveDir)%(Filename)%(Extension)" /&gt;</code>
/// The platform serves them at <c>/modules/{moduleId}/{path}</c> under the same license rules as the module's endpoints
/// and lists the entry in <c>/api/ui/modules</c>. The entry module exports <c>register(host)</c>; it imports React,
/// React Router and <c>@vyxum/sdk</c> as bare specifiers, which the web application maps to its own copies.
/// </remarks>
public sealed class ModuleUi
{
    /// <summary>Logical name prefix of the embedded front end files.</summary>
    public const string ResourcePrefix = "vyxum-ui/";

    /// <summary>Path of the entry ES module, relative to <see cref="ResourcePrefix"/>, e.g. <c>index.js</c>.</summary>
    public required string Entry { get; init; }

    /// <summary>
    /// Version of the web application's module SDK (<c>@vyxum/sdk</c>) the front end was built against, 1 or more.
    /// The web application does not load a module that needs a newer SDK than it has.
    /// </summary>
    public required int SdkVersion { get; init; }
}
