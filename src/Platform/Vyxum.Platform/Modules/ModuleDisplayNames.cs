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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var known = strings.GetStrings(CultureInfo.InvariantCulture);
        var missing = catalog.Modules
            .Where(m => m.Manifest.DisplayNameKey is { } key && !known.ContainsKey(key))
            .Select(m => $"module '{m.Manifest.Id}': '{m.Manifest.DisplayNameKey}'")
            .ToList();
        return missing.Count == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException(
                "Display name keys that are not in any registered UI string set: " + string.Join(", ", missing) + ".");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
