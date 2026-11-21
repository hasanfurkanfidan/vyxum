using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Vyxum.Agent.Configuration;
using Vyxum.Platform;
using Xunit;
using static Vyxum.IntegrationTests.AgentTestSupport;

namespace Vyxum.IntegrationTests;

public sealed class AgentSharedSettingsTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-agent-shared-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Agent_and_platform_agree_on_the_settings_file_path()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DatabaseSettings.DataDirectoryKey] = _dataDirectory })
            .Build();

        SharedSettingsFile.GetPath(configuration).ShouldBe(VyxumSettingsFile.FromConfiguration(configuration).Path);
        SharedSettingsFile.DataDirectoryKey.ShouldBe(DatabaseSettings.DataDirectoryKey);
    }

    [Fact]
    public void Agent_reads_the_shared_settings_file_and_explicit_settings_win()
    {
        new VyxumSettingsFile(Path.Combine(_dataDirectory, VyxumSettingsFile.FileName)).Update(new Dictionary<string, string?>
        {
            ["Vyxum:Agent:AgentName"] = "from-settings-file",
            ["Vyxum:Agent:ServerUrl"] = "http://localhost:5999",
        });
        var settings = AgentConfig("http://localhost:5123", key: null, new SimulatedDevice("scale-1", 1m, "kg"));
        settings.Remove("Vyxum:Agent:AgentName");
        settings[SharedSettingsFile.DataDirectoryKey] = _dataDirectory;

        using var host = BuildAgent(settings);
        var agent = host.Services.GetRequiredService<IOptions<AgentSettings>>().Value;

        agent.AgentName.ShouldBe("from-settings-file");
        agent.ServerUrl.ShouldBe("http://localhost:5123");
    }
}
