using System.Globalization;
using Microsoft.Extensions.Hosting;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// Resolves the display name each module declares with <see cref="ModuleManifest.DisplayNameKey"/> from the UI strings,
/// and checks at startup that every declared key exists, so a typo fails at once and not on the license screen.
/// </summary>
internal sealed class ModuleDisplayNames(ModuleCatalog catalog, UiStringCatalog strings) : IHostedService
{
    /// <summary>The name of <paramref name="manifest"/> in <paramref name="culture"/>, or null when it declares none.</summary>
    public string? Resolve(ModuleManifest manifest, CultureInfo culture) =>
        manifest.DisplayNameKey is { } key && strings.GetStrings(culture).TryGetValue(key, out var name) ? name : null;

    /// <summary>The preview description of <paramref name="manifest"/> in <paramref name="culture"/>, or null when it declares no preview.</summary>
    public string? ResolveDescription(ModuleManifest manifest, CultureInfo culture) =>
        manifest.Preview is { } preview && strings.GetStrings(culture).TryGetValue(preview.DescriptionKey, out var text) ? text : null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var known = strings.GetStrings(CultureInfo.InvariantCulture);
        var missing = catalog.Modules
            .Where(m => m.Manifest.DisplayNameKey is { } key && !known.ContainsKey(key))
            .Select(m => $"module '{m.Manifest.Id}': '{m.Manifest.DisplayNameKey}'")
            .Concat(catalog.Modules
                .Where(m => m.Manifest.Preview is { } preview && !known.ContainsKey(preview.DescriptionKey))
                .Select(m => $"module '{m.Manifest.Id}' preview: '{m.Manifest.Preview!.DescriptionKey}'"))
            .Concat(catalog.Modules
                .SelectMany(m => m.Manifest.Permissions.Select(p => (Module: m.Manifest.Id, Permission: p)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Permission.NameKey) && !known.ContainsKey(x.Permission.NameKey))
                .Select(x => $"module '{x.Module}' permission '{x.Permission.Id}': '{x.Permission.NameKey}'"))
            .ToList();
        return missing.Count == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException(
                "Display name, preview description and permission name keys that are not in any registered UI string set: " + string.Join(", ", missing) + ".");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
