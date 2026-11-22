using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace Vyxum.Platform;

/// <summary>A glossary entry in one language, with its name in the other languages (for search across languages).</summary>
public sealed record GlossaryTerm(
    string Id,
    string Name,
    string Text,
    string? InVyxum,
    string? Example,
    IReadOnlyList<GlossaryName> OtherNames);

/// <summary>The name of a term in another language.</summary>
public sealed record GlossaryName(string Culture, string Name);

/// <summary>A section of the glossary with its terms in order.</summary>
public sealed record GlossarySection(string Id, string Title, string Intro, IReadOnlyList<GlossaryTerm> Terms);

/// <summary>
/// Reads the glossary from a resource file. The neutral (English) resources define the sections and terms by their
/// keys (see <see cref="GlossaryStrings.Terms"/>); a culture's resources translate them, and a missing translation
/// keeps the English text. Sections and terms are ordered by their numbers. Results are cached per culture.
/// </summary>
public sealed partial class GlossaryCatalog
{
    private readonly ResourceManager _resources;
    private readonly IReadOnlyList<CultureInfo> _languages;
    private readonly IReadOnlyList<SectionKeys> _keys;
    private readonly ConcurrentDictionary<string, IReadOnlyList<GlossarySection>> _cache = new(StringComparer.Ordinal);

    /// <summary>Creates the catalog.</summary>
    /// <param name="resources">The glossary resources.</param>
    /// <param name="languages">The languages of the user interface; a term lists its name in each of them except the requested one.</param>
    /// <exception cref="InvalidOperationException">A key does not follow the key pattern, or a term or section misses a required key.</exception>
    public GlossaryCatalog(ResourceManager resources, IEnumerable<CultureInfo> languages)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(languages);

        _resources = resources;
        _languages = [.. languages];
        _keys = ReadKeys(resources);
    }

    /// <summary>The glossary in <paramref name="culture"/>.</summary>
    public IReadOnlyList<GlossarySection> GetSections(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return _cache.GetOrAdd(culture.Name, _ => Build(culture));
    }

    private IReadOnlyList<GlossarySection> Build(CultureInfo culture)
    {
        var others = _languages
            .Where(l => !string.Equals(l.TwoLetterISOLanguageName, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return [.. _keys.Select(section => new GlossarySection(
            section.Id,
            Text(section.Id + ".Title", culture),
            Text(section.Id + ".Intro", culture),
            [.. section.Terms.Select(term =>
            {
                var name = Text(term.Id + ".Name", culture);
                return new GlossaryTerm(
                    term.Id,
                    name,
                    Text(term.Id + ".Text", culture),
                    term.HasInVyxum ? Text(term.Id + ".InVyxum", culture) : null,
                    term.HasExample ? Text(term.Id + ".Example", culture) : null,
                    [.. others
                        .Select(l => new GlossaryName(l.TwoLetterISOLanguageName, Text(term.Id + ".Name", l)))
                        .Where(n => !string.Equals(n.Name, name, StringComparison.Ordinal))]);
            })]))];
    }

    // GetString falls back through the parent cultures to the neutral resources.
    private string Text(string key, CultureInfo culture) => _resources.GetString(key, culture) ?? string.Empty;

    private static List<SectionKeys> ReadKeys(ResourceManager resources)
    {
        var set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException("The glossary resources were not found.");
        var keys = set.Cast<DictionaryEntry>().Where(e => e.Value is string).Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);

        var sections = new SortedDictionary<int, SortedDictionary<int, string>>();
        foreach (var key in keys)
        {
            if (SectionKey().Match(key) is { Success: true } s)
            {
                sections.TryAdd(int.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture), []);
            }
            else if (TermKey().Match(key) is { Success: true } t)
            {
                var section = int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture);
                var number = int.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture);
                if (!sections.TryGetValue(section, out var terms))
                {
                    sections[section] = terms = [];
                }

                terms[number] = $"T{t.Groups[1].Value}.{t.Groups[2].Value}";
            }
            else
            {
                throw new InvalidOperationException($"The glossary key '{key}' does not follow the pattern S<n>.Title|Intro or T<n>.<nn>.Name|Text|InVyxum|Example.");
            }
        }

        var result = new List<SectionKeys>();
        foreach (var (number, terms) in sections)
        {
            var id = "S" + number.ToString(CultureInfo.InvariantCulture);
            Require(keys, id + ".Title");
            Require(keys, id + ".Intro");
            var termKeys = new List<TermKeys>();
            foreach (var termId in terms.Values)
            {
                Require(keys, termId + ".Name");
                Require(keys, termId + ".Text");
                termKeys.Add(new TermKeys(termId, keys.Contains(termId + ".InVyxum"), keys.Contains(termId + ".Example")));
            }

            result.Add(new SectionKeys(id, termKeys));
        }

        return result;
    }

    private static void Require(HashSet<string> keys, string key)
    {
        if (!keys.Contains(key))
        {
            throw new InvalidOperationException($"The glossary key '{key}' is missing.");
        }
    }

    [GeneratedRegex(@"^S(\d+)\.(Title|Intro)$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionKey();

    [GeneratedRegex(@"^T(\d+)\.(\d+)\.(Name|Text|InVyxum|Example)$", RegexOptions.CultureInvariant)]
    private static partial Regex TermKey();

    private sealed record SectionKeys(string Id, IReadOnlyList<TermKeys> Terms);

    private sealed record TermKeys(string Id, bool HasInVyxum, bool HasExample);
}
