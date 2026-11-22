using Shouldly;
using Xunit;

namespace Vyxum.ArchitectureTests;

/// <summary>The open repository never contains private keys or license files (CLAUDE.md, security rules).</summary>
public class RepositoryHygieneTests
{
    private static readonly string[] IgnoredDirectories = ["bin", "obj", "node_modules", ".git", "dist", "TestResults"];
    private static readonly string[] ForbiddenExtensions = [".pem", ".key", ".pfx", ".p12", ".vxlic"];
    private static readonly string[] RequiredIgnorePatterns = ["*.pem", "*.key", "*.pfx", "*.p12"];

    // Built from parts so this file does not contain the markers themselves.
    private static readonly string PrivateKeyMarker = "PRIVATE KEY" + "-----";

    // The base64 (DER) start of an unencrypted EC private key without PEM armour: PKCS#8 of a P-256 key
    // (SEQUENCE, version 0, id-ecPublicKey, prime256v1, OCTET STRING) and SEC1 (SEQUENCE, version 1, OCTET STRING).
    private static readonly string[] Base64PrivateKeyMarkers =
    [
        "MIGHAgEAMBMGByqGSM49" + "AgEGCCqGSM49AwEHBG0w",
        "MHcCAQEE" + "I",
    ];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (IgnoredDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Files(child))
            {
                yield return file;
            }
        }
    }

    [Fact]
    public void No_key_or_license_files_exist_in_the_repository()
    {
        var root = RepositoryRoot();
        var found = Files(root)
            .Where(f => ForbiddenExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        found.ShouldBeEmpty();
    }

    [Fact]
    public void No_file_contains_a_private_key_block()
    {
        var root = RepositoryRoot();
        var found = new List<string>();
        foreach (var file in Files(root))
        {
            if (new FileInfo(file).Length > 5_000_000)
            {
                continue;
            }

            try
            {
                if (ContainsPrivateKey(File.ReadAllText(file)))
                {
                    found.Add(Path.GetRelativePath(root, file));
                }
            }
            catch (IOException)
            {
                // A file that cannot be read is not scanned.
            }
        }

        found.ShouldBeEmpty();
    }

    [Fact]
    public void The_scan_detects_pem_and_base64_ec_private_keys()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);

        ContainsPrivateKey(key.ExportPkcs8PrivateKeyPem()).ShouldBeTrue();
        ContainsPrivateKey(key.ExportECPrivateKeyPem()).ShouldBeTrue();
        ContainsPrivateKey("\"key\": \"" + Convert.ToBase64String(key.ExportPkcs8PrivateKey()) + "\"").ShouldBeTrue();
        ContainsPrivateKey("key=" + Convert.ToBase64String(key.ExportECPrivateKey())).ShouldBeTrue();
        ContainsPrivateKey(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())).ShouldBeFalse();
        ContainsPrivateKey(key.ExportSubjectPublicKeyInfoPem()).ShouldBeFalse();
    }

    private static bool ContainsPrivateKey(string text) =>
        text.Contains(PrivateKeyMarker, StringComparison.Ordinal)
        || Base64PrivateKeyMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));

    [Fact]
    public void Gitignore_excludes_key_files()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), ".gitignore")).Select(l => l.Trim()).ToList();

        foreach (var pattern in RequiredIgnorePatterns)
        {
            lines.ShouldContain(pattern);
        }
    }
}
