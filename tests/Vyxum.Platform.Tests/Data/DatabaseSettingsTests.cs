using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Vyxum.Platform.Tests.Data;

public class DatabaseSettingsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Zero_config_uses_sqlite_file_in_default_data_directory()
    {
        var settings = DatabaseSettings.FromConfiguration(Config());

        settings.Provider.ShouldBe(DatabaseProvider.Sqlite);
        var expectedDirectory = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vyxum")
            : Path.GetFullPath("data");
        new SqliteConnectionStringBuilder(settings.ConnectionString).DataSource
            .ShouldBe(Path.Combine(expectedDirectory, "vyxum.db"));
    }

    [Fact]
    public void Configured_data_directory_holds_the_sqlite_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vyxum-data");

        var settings = DatabaseSettings.FromConfiguration(Config((DatabaseSettings.DataDirectoryKey, directory)));

        new SqliteConnectionStringBuilder(settings.ConnectionString).DataSource.ShouldBe(Path.Combine(directory, "vyxum.db"));
    }

    [Fact]
    public void Provider_and_connection_string_are_read_case_insensitively()
    {
        var settings = DatabaseSettings.FromConfiguration(Config(
            (DatabaseSettings.ProviderKey, "sqlserver"),
            (DatabaseSettings.ConnectionStringKey, "Server=db;Database=vyxum")));

        settings.ShouldBe(new DatabaseSettings(DatabaseProvider.SqlServer, "Server=db;Database=vyxum"));
    }

    [Fact]
    public void SqlServer_without_connection_string_fails_clearly()
    {
        Should.Throw<InvalidOperationException>(() =>
                DatabaseSettings.FromConfiguration(Config((DatabaseSettings.ProviderKey, "SqlServer"))))
            .Message.ShouldContain("'Vyxum:Database:ConnectionString' is required");
    }

    [Theory]
    [InlineData("Oracle")]
    [InlineData("5")]
    public void Unknown_provider_fails_clearly(string provider)
    {
        Should.Throw<InvalidOperationException>(() =>
                DatabaseSettings.FromConfiguration(Config((DatabaseSettings.ProviderKey, provider))))
            .Message.ShouldContain($"unknown value '{provider}'");
    }

    [Fact]
    public void ToString_hides_connection_string()
    {
        new DatabaseSettings(DatabaseProvider.SqlServer, "Password=secret").ToString().ShouldNotContain("secret");
    }
}
