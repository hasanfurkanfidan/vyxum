using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// Merges every registered <see cref="UiStringSource"/> into one flat table per culture, with keys
/// <c>Prefix.Key</c>. The neutral (English) resources define the keys; a culture's resources
/// translate them, and a key missing in a culture keeps its English value. Results are cached.
/// </summary>
public sealed class UiStringCatalog
{
    private readonly IReadOnlyList<UiStringSource> _sources;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _cache = new(StringComparer.Ordinal);

    /// <summary>Creates the catalog.</summary>
    /// <exception cref="InvalidOperationException">Two sources use the same prefix.</exception>
    public UiStringCatalog(IEnumerable<UiStringSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = [.. sources.OrderBy(s => s.Prefix, StringComparer.Ordinal)];
        var duplicates = _sources.GroupBy(s => s.Prefix, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "UI string prefixes must be unique; registered more than once: " + string.Join(", ", duplicates.Select(p => $"'{p}'")) + ".");
        }
    }

    /// <summary>All strings in <paramref name="culture"/>, keyed <c>Prefix.Key</c> and ordered by key.</summary>
    public IReadOnlyDictionary<string, string> GetStrings(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return _cache.GetOrAdd(culture.Name, _ => Build(culture));
    }

    private IReadOnlyDictionary<string, string> Build(CultureInfo culture)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in _sources)
        {
            foreach (var key in NeutralKeys(source.Resources))
            {
                // GetString falls back through the parent cultures to the neutral resources.
                result[source.Prefix + "." + key] = source.Resources.GetString(key, culture) ?? string.Empty;
            }
        }

        return result;
    }

    private static IEnumerable<string> NeutralKeys(ResourceManager resources)
    {
        var set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        if (set is null)
        {
            return [];
        }

        return [.. set.Cast<DictionaryEntry>().Where(e => e.Value is string).Select(e => (string)e.Key)];
    }
}
