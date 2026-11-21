using System.Resources;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Vyxum.Platform.Abstractions;

/// <summary>
/// User interface strings of a module or of the host (decision V-33). Register one per resource
/// file with <see cref="UiStringServiceCollectionExtensions.AddUiStrings"/>; the platform serves
/// every key as <c>Prefix.Key</c> from <c>GET /api/ui/strings</c>.
/// </summary>
/// <remarks>
/// The neutral resources (<c>Strings.resx</c>) are English and define the key set; a culture's
/// resources (<c>Strings.tr.resx</c>) translate them. A key missing in a culture falls back to English.
/// </remarks>
public sealed partial record UiStringSource
{
    /// <summary>Creates a source.</summary>
    /// <param name="prefix">Lowercase prefix such as the module id, e.g. <c>products</c>. The host uses <c>shell</c>.</param>
    /// <param name="resources">The resource manager of the module's <c>Strings.resx</c>.</param>
    /// <exception cref="ArgumentException">The prefix is not lowercase dot-separated kebab-case.</exception>
    public UiStringSource(string prefix, ResourceManager resources)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(resources);
        if (!PrefixPattern().IsMatch(prefix))
        {
            throw new ArgumentException(
                $"UI string prefix '{prefix}' must be lowercase, dot-separated kebab-case segments, e.g. 'products'.",
                nameof(prefix));
        }

        Prefix = prefix;
        Resources = resources;
    }

    /// <summary>The key prefix.</summary>
    public string Prefix { get; }

    /// <summary>The resource manager that holds the strings.</summary>
    public ResourceManager Resources { get; }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*(\\.[a-z][a-z0-9]*(-[a-z0-9]+)*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}

/// <summary>Registers <see cref="UiStringSource"/> instances.</summary>
public static class UiStringServiceCollectionExtensions
{
    /// <summary>Registers the strings of <paramref name="resources"/> under <paramref name="prefix"/> as a singleton.</summary>
    /// <exception cref="ArgumentException">The prefix is invalid; see <see cref="UiStringSource"/>.</exception>
    public static IServiceCollection AddUiStrings(this IServiceCollection services, string prefix, ResourceManager resources)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton(new UiStringSource(prefix, resources));
    }
}
