using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Vyxum.ArchitectureTests;

/// <summary>Every user-facing string exists in English and Turkish (CLAUDE.md, decision V-33).</summary>
public class ResourceFileTests
{
    private static readonly string[] IgnoredDirectories = ["bin", "obj", "node_modules"];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private static IEnumerable<string> EnglishResourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "Strings.resx"))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (IgnoredDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in EnglishResourceFiles(child))
            {
                yield return file;
            }
        }
    }

    private static Dictionary<string, string> ReadStrings(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .Where(d => d.Attribute("type") is null)
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")?.Value ?? string.Empty, StringComparer.Ordinal);

    [Fact]
    public void Source_tree_has_resource_files()
    {
        EnglishResourceFiles(Path.Combine(RepositoryRoot(), "src")).ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_strings_resx_has_a_turkish_file_with_the_same_keys_and_no_empty_values()
    {
        var root = RepositoryRoot();
        var problems = new List<string>();
        foreach (var english in EnglishResourceFiles(Path.Combine(root, "src")))
        {
            var relative = Path.GetRelativePath(root, english);
            var turkish = Path.Combine(Path.GetDirectoryName(english)!, "Strings.tr.resx");
            if (!File.Exists(turkish))
            {
                problems.Add($"{relative}: Strings.tr.resx is missing.");
                continue;
            }

            var en = ReadStrings(english);
            var tr = ReadStrings(turkish);
            problems.AddRange(en.Keys.Except(tr.Keys).Order(StringComparer.Ordinal).Select(k => $"{relative}: '{k}' is missing in Strings.tr.resx."));
            problems.AddRange(tr.Keys.Except(en.Keys).Order(StringComparer.Ordinal).Select(k => $"{relative}: '{k}' exists only in Strings.tr.resx."));
            problems.AddRange(en.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => $"{relative}: '{p.Key}' is empty."));
            problems.AddRange(tr.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => $"{relative}: '{p.Key}' is empty in Strings.tr.resx."));
        }

        problems.ShouldBeEmpty();
    }
}
