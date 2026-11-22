using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Vyxum.Platform.Tests.Security;

public sealed class AgentKeyProvisioningTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vyxum-agentkey-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, VyxumSettingsFile.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private bool Ensure()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(FilePath, optional: true).Build();
        return AgentKeyProvisioning.EnsureKey(new VyxumSettingsFile(FilePath), configuration);
    }

    private JsonObject ReadFile() => JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();

    [Fact]
    public void A_new_installation_gets_the_same_random_key_for_web_and_agent_and_keeps_other_settings()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{"Urls":"http://localhost:5000","Vyxum":{"Agent":{"ServerUrl":"http://localhost:5000"}}}""");

        Ensure().ShouldBeTrue();

        var json = ReadFile();
        var server = json["Vyxum"]!["Agents"]!["Key"]!.GetValue<string>();
        server.Length.ShouldBe(43);
        json["Vyxum"]!["Agent"]!["Key"]!.GetValue<string>().ShouldBe(server);
        json["Vyxum"]!["Agent"]!["ServerUrl"]!.GetValue<string>().ShouldBe("http://localhost:5000");
        json["Urls"]!.GetValue<string>().ShouldBe("http://localhost:5000");
    }

    [Fact]
    public void An_existing_key_is_never_changed()
    {
        Ensure().ShouldBeTrue();
        var first = File.ReadAllText(FilePath);

        Ensure().ShouldBeFalse();

        File.ReadAllText(FilePath).ShouldBe(first);
    }

    [Fact]
    public void An_agent_without_a_key_gets_the_existing_one()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{"Vyxum":{"Agents":{"Key":"chosen-by-the-customer"}}}""");

        Ensure().ShouldBeTrue();

        var json = ReadFile();
        json["Vyxum"]!["Agents"]!["Key"]!.GetValue<string>().ShouldBe("chosen-by-the-customer");
        json["Vyxum"]!["Agent"]!["Key"]!.GetValue<string>().ShouldBe("chosen-by-the-customer");
    }

    [Fact]
    public void Keys_are_random()
    {
        AgentKeyProvisioning.NewKey().ShouldNotBe(AgentKeyProvisioning.NewKey());
        AgentKeyProvisioning.NewKey().ShouldMatch("^[A-Za-z0-9_-]{43}$");
    }
}
