using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Endpoint metadata: this endpoint of a module that requires a license keeps accepting writes while the module's
/// license is <see cref="ModuleLicenseState.ReadOnly"/>. Meant for the few writes that only let people reach what
/// read-only mode still shows, such as signing in; it changes nothing in the other states.
/// </summary>
public sealed class AllowedWhileReadOnly
{
    private AllowedWhileReadOnly()
    {
    }

    /// <summary>The single instance added as metadata.</summary>
    public static AllowedWhileReadOnly Instance { get; } = new();
}

/// <summary>Marks module endpoints with <see cref="AllowedWhileReadOnly"/>.</summary>
public static class AllowedWhileReadOnlyExtensions
{
    /// <summary>Lets the endpoints of <paramref name="builder"/> accept writes while the module's license is read-only.</summary>
    public static TBuilder AllowWhileReadOnly<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(AllowedWhileReadOnly.Instance);
}
