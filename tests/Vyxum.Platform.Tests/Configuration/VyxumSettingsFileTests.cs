using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Vyxum.Platform.Tests.Configuration;

public sealed class VyxumSettingsFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vyxum-settings-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, VyxumSettingsFile.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonObject ReadFile() => JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();

    [Fact]
    public void Update_creates_the_file_and_its_directory_with_nested_keys()
    {
        new VyxumSettingsFile(FilePath).Update(new Dictionary<string, string?>
        {
            [DatabaseSettings.ProviderKey] = "SqlServer",
            [DatabaseSettings.ConnectionStringKey] = "Server=x;Database=y",
        });

        var json = ReadFile();
        json["Vyxum"]!["Database"]!["Provider"]!.GetValue<string>().ShouldBe("SqlServer");
        json["Vyxum"]!["Database"]!["ConnectionString"]!.GetValue<string>().ShouldBe("Server=x;Database=y");
    }

    [Fact]
    public void Update_merges_and_keeps_unknown_keys()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            {
              // written by the installer
              "Urls": "http://localhost:5000",
              "vyxum": { "Agent": { "ServerUrl": "http://localhost:5000" }, "Desktop": { "FullScreen": true } },
              "Custom": [1, 2, 3],
            }
            """);

        new VyxumSettingsFile(FilePath).Update(new Dictionary<string, string?> { [DatabaseSettings.ProviderKey] = "SqlServer" });

        var json = ReadFile();
        json["Urls"]!.GetValue<string>().ShouldBe("http://localhost:5000");
        json["Custom"]!.AsArray().Count.ShouldBe(3);
        json["vyxum"]!["Agent"]!["ServerUrl"]!.GetValue<string>().ShouldBe("http://localhost:5000");
        json["vyxum"]!["Desktop"]!["FullScreen"]!.GetValue<bool>().ShouldBeTrue();
        json["vyxum"]!["Database"]!["Provider"]!.GetValue<string>().ShouldBe("SqlServer");
        json.ContainsKey("Vyxum").ShouldBeFalse("keys are matched case-insensitively, like configuration");
    }

    [Fact]
    public void Null_removes_the_key_and_empty_parents_and_flat_keys()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            { "Vyxum:Database:ConnectionString": "flat", "Vyxum": { "Database": { "Provider": "SqlServer" }, "Agent": { "Key": "k" } } }
            """);

        new VyxumSettingsFile(FilePath).Update(new Dictionary<string, string?>
        {
            [DatabaseSettings.ProviderKey] = null,
            [DatabaseSettings.ConnectionStringKey] = null,
        });

        var json = ReadFile();
        json.ContainsKey("Vyxum:Database:ConnectionString").ShouldBeFalse();
        json["Vyxum"]!.AsObject().ContainsKey("Database").ShouldBeFalse();
        json["Vyxum"]!["Agent"]!["Key"]!.GetValue<string>().ShouldBe("k");
    }

    [Fact]
    public void Update_replaces_the_file_atomically_and_leaves_no_temporary_file()
    {
        var file = new VyxumSettingsFile(FilePath);
        for (var i = 0; i < 20; i++)
        {
            file.Update(new Dictionary<string, string?> { ["Vyxum:Test:Counter"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        Directory.GetFiles(_directory).Select(Path.GetFileName).ShouldBe([VyxumSettingsFile.FileName]);
        ReadFile()["Vyxum"]!["Test"]!["Counter"]!.GetValue<string>().ShouldBe("19");
    }

    [Fact]
    public void Invalid_existing_file_is_not_overwritten()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ not json");

        Should.Throw<InvalidDataException>(() =>
            new VyxumSettingsFile(FilePath).Update(new Dictionary<string, string?> { [DatabaseSettings.ProviderKey] = "Sqlite" }));

        File.ReadAllText(FilePath).ShouldBe("{ not json");
    }

    [Fact]
    public void Path_follows_the_configured_data_directory()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DatabaseSettings.DataDirectoryKey] = _directory })
            .Build();

        VyxumSettingsFile.FromConfiguration(configuration).Path.ShouldBe(FilePath);
    }

    [Fact]
    public void Added_source_overrides_appsettings_but_not_later_sources()
    {
        Directory.CreateDirectory(_directory);
        var appsettings = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(appsettings, """{ "A": "appsettings", "B": "appsettings", "C": "appsettings" }""");
        File.WriteAllText(FilePath, """{ "A": "vyxum", "B": "vyxum" }""");

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { [DatabaseSettings.DataDirectoryKey] = _directory });
        configuration.AddJsonFile(appsettings);
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["B"] = "command line" });

        VyxumSettingsFile.AddTo(configuration);

        configuration["A"].ShouldBe("vyxum");
        configuration["B"].ShouldBe("command line");
        configuration["C"].ShouldBe("appsettings");
    }
}
