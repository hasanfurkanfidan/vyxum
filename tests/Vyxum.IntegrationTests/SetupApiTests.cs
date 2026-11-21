using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.IntegrationTests;

public sealed class SetupApiTests : IDisposable
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("192.168.1.50");

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-setup-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRestarter _restarter = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string SettingsFilePath => Path.Combine(_dataDirectory, VyxumSettingsFile.FileName);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly; the temp folder is disposable anyway.
        }
    }

    private WebApplicationFactory<Program> CreateFactory(TestDatabase? database = null, IPAddress? remoteAddress = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory);
            if (database is not null)
            {
                builder.UseSetting(DatabaseSettings.ProviderKey, database.Provider.ToString());
                builder.UseSetting(DatabaseSettings.ConnectionStringKey, database.ConnectionString);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHostRestarter>(_restarter);
                if (remoteAddress is not null)
                {
                    services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(remoteAddress));
                }
            });
        });

    private static async Task<JsonElement> GetStatusAsync(HttpClient client)
    {
        using var response = await client.GetAsync(SetupEndpointExtensions.StatusPath, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.Clone();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string path, object? body = null)
    {
        using var response = await client.PostAsJsonAsync(path, body ?? new { }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        using var json = JsonDocument.Parse(text.Length == 0 ? "null" : text);
        return (response.StatusCode, json.RootElement.Clone());
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Setup_starts_incomplete_and_company_then_complete_finishes_it(DatabaseProvider provider)
    {
        await using var database = provider == DatabaseProvider.SqlServer ? Databases.CreateOrSkip(provider) : null;
        await using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        var status = await GetStatusAsync(client);
        status.GetProperty("completed").GetBoolean().ShouldBeFalse();
        status.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
        status.GetProperty("version").GetString()!.ShouldNotContain("+");
        status.GetProperty("database").GetProperty("provider").GetString().ShouldBe(provider.ToString());
        status.GetProperty("database").GetProperty("isDefault").GetBoolean().ShouldBe(provider == DatabaseProvider.Sqlite);
        status.GetProperty("company").GetProperty("name").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("dataDirectory").GetString().ShouldBe(Path.GetFullPath(_dataDirectory));
        status.GetProperty("restartPending").GetBoolean().ShouldBeFalse();

        var (companyStatus, company) = await PostAsync(client, SetupEndpointExtensions.CompanyPath, new { name = "  Örnek Gıda  " });
        companyStatus.ShouldBe(HttpStatusCode.OK);
        company.GetProperty("name").GetString().ShouldBe("Örnek Gıda");

        var (completeStatus, completed) = await PostAsync(client, SetupEndpointExtensions.CompletePath);
        completeStatus.ShouldBe(HttpStatusCode.OK);
        completed.GetProperty("completed").GetBoolean().ShouldBeTrue();
        var completedAt = completed.GetProperty("completedAt").GetDateTime();

        status = await GetStatusAsync(client);
        status.GetProperty("completed").GetBoolean().ShouldBeTrue();
        status.GetProperty("company").GetProperty("name").GetString().ShouldBe("Örnek Gıda");

        using var modules = JsonDocument.Parse(await client.GetStringAsync(UiEndpointExtensions.ModulesPath, Ct));
        modules.RootElement.GetProperty("companyName").GetString().ShouldBe("Örnek Gıda");

        // Complete stays idempotent; every other change is refused from now on.
        var (againStatus, again) = await PostAsync(client, SetupEndpointExtensions.CompletePath);
        againStatus.ShouldBe(HttpStatusCode.OK);
        again.GetProperty("completedAt").GetDateTime().ShouldBe(completedAt);

        foreach (var (path, body) in new (string, object)[]
                 {
                     (SetupEndpointExtensions.CompanyPath, new { name = "Başka" }),
                     (SetupEndpointExtensions.DatabasePath, new { provider = "Sqlite" }),
                     (SetupEndpointExtensions.DatabaseTestPath, new { provider = "Sqlite" }),
                 })
        {
            var (conflict, problem) = await PostAsync(client, path, body);
            conflict.ShouldBe(HttpStatusCode.Conflict, path);
            problem.GetProperty("code").GetString().ShouldBe("setup.AlreadyCompleted");
        }

        (await GetStatusAsync(client)).GetProperty("company").GetProperty("name").GetString().ShouldBe("Örnek Gıda");
    }

    [Fact]
    public async Task Remote_callers_cannot_change_setup_and_do_not_see_local_paths()
    {
        await using var factory = CreateFactory(remoteAddress: RemoteAddress);
        using var client = factory.CreateClient();

        var status = await GetStatusAsync(client);
        status.GetProperty("completed").GetBoolean().ShouldBeFalse();
        status.GetProperty("dataDirectory").ValueKind.ShouldBe(JsonValueKind.Null);
        status.ToString().ShouldNotContain(_dataDirectory);

        foreach (var (path, body) in new (string, object)[]
                 {
                     (SetupEndpointExtensions.CompanyPath, new { name = "X" }),
                     (SetupEndpointExtensions.CompletePath, new { }),
                     (SetupEndpointExtensions.DatabasePath, new { provider = "Sqlite" }),
                     (SetupEndpointExtensions.DatabaseTestPath, new { provider = "Sqlite" }),
                 })
        {
            var (forbidden, problem) = await PostAsync(client, path, body);
            forbidden.ShouldBe(HttpStatusCode.Forbidden, path);
            problem.GetProperty("code").GetString().ShouldBe("setup.LocalOnly");
        }

        (await GetStatusAsync(client)).GetProperty("completed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Last_backup_path_is_only_shown_to_local_callers()
    {
        var backups = Path.Combine(_dataDirectory, "backups");
        Directory.CreateDirectory(backups);
        var backup = Path.Combine(backups, "vyxum-20261121-100000-test.db");
        await File.WriteAllTextAsync(backup, "x", Ct);

        await using (var local = CreateFactory())
        {
            using var client = local.CreateClient();
            var lastBackup = (await GetStatusAsync(client)).GetProperty("lastBackup");
            lastBackup.GetProperty("path").GetString().ShouldBe(backup);
            lastBackup.GetProperty("at").GetDateTime().ShouldBe(File.GetLastWriteTimeUtc(backup), TimeSpan.FromSeconds(1));
        }

        await using var remote = CreateFactory(remoteAddress: RemoteAddress);
        using var remoteClient = remote.CreateClient();
        var remoteBackup = (await GetStatusAsync(remoteClient)).GetProperty("lastBackup");
        remoteBackup.GetProperty("path").ValueKind.ShouldBe(JsonValueKind.Null);
        remoteBackup.GetProperty("at").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Theory]
    [InlineData("""{ "name": "" }""", "setup.CompanyNameRequired")]
    [InlineData("""{ "name": "   " }""", "setup.CompanyNameRequired")]
    [InlineData("""{ }""", "setup.CompanyNameRequired")]
    public async Task Company_name_is_validated(string body, string code)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(SetupEndpointExtensions.CompanyPath, content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("errors").GetProperty("name")[0].GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Too_long_company_name_is_rejected()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (status, body) = await PostAsync(client, SetupEndpointExtensions.CompanyPath, new { name = new string('a', 201) });

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("errors").GetProperty("name")[0].GetString().ShouldBe("setup.CompanyNameTooLong");
    }

    [Theory]
    [InlineData("Oracle", null, "provider", "setup.ProviderInvalid")]
    [InlineData("1", null, "provider", "setup.ProviderInvalid")]
    [InlineData("SqlServer", null, "connectionString", "setup.ConnectionStringRequired")]
    [InlineData("SqlServer", "this is not a connection string", "connectionString", "setup.ConnectionStringInvalid")]
    [InlineData("SqlServer", "Server=localhost;Integrated Security=true", "connectionString", "setup.DatabaseNameRequired")]
    public async Task Database_input_is_validated(string provider, string? connectionString, string field, string code)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[] { SetupEndpointExtensions.DatabaseTestPath, SetupEndpointExtensions.DatabasePath })
        {
            var (status, body) = await PostAsync(client, path, new { provider, connectionString });
            status.ShouldBe(HttpStatusCode.BadRequest, path);
            body.GetProperty("errors").GetProperty(field)[0].GetString().ShouldBe(code, path);
        }

        File.Exists(SettingsFilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Database_test_with_an_unreachable_server_reports_server_not_found_quickly()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        const string password = "S3cret-not-echoed";
        var stopwatch = Stopwatch.StartNew();

        using var response = await client.PostAsJsonAsync(
            SetupEndpointExtensions.DatabaseTestPath,
            new { provider = "SqlServer", connectionString = $"Server=tcp:127.0.0.1,1;Database=Vyxum;User Id=sa;Password={password};TrustServerCertificate=True" },
            Ct);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(Ct);
        text.ShouldNotContain(password);
        using var json = JsonDocument.Parse(text);
        json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
        json.RootElement.GetProperty("code").GetString().ShouldBe("setup.DbServerNotFound");
    }

    [Fact]
    public async Task Selecting_an_unreachable_sql_server_is_refused_and_writes_nothing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (status, body) = await PostAsync(client, SetupEndpointExtensions.DatabasePath,
            new { provider = "SqlServer", connectionString = "Server=tcp:127.0.0.1,1;Database=Vyxum;Integrated Security=true;TrustServerCertificate=True" });

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("errors").GetProperty("connectionString")[0].GetString().ShouldBe("setup.DbServerNotFound");
        File.Exists(SettingsFilePath).ShouldBeFalse();
        _restarter.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Database_test_of_sqlite_succeeds()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (status, body) = await PostAsync(client, SetupEndpointExtensions.DatabaseTestPath, new { provider = "sqlite" });

        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("ok").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Selecting_the_current_database_changes_nothing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (status, body) = await PostAsync(client, SetupEndpointExtensions.DatabasePath, new { provider = "Sqlite" });

        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("restartRequired").GetBoolean().ShouldBeFalse();
        File.Exists(SettingsFilePath).ShouldBeFalse();
        _restarter.Calls.ShouldBe(0);
        (await GetStatusAsync(client)).GetProperty("restartPending").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Selecting_sql_server_writes_the_settings_file_and_restarts()
    {
        await using var database = Databases.CreateOrSkip(DatabaseProvider.SqlServer);
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(SettingsFilePath, """
            {"Urls":"http://localhost:5123","Vyxum":{"Agent":{"ServerUrl":"http://localhost:5123"},"Unknown":{"Keep":"me"}}}
            """, Ct);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var (status, body) = await PostAsync(client, SetupEndpointExtensions.DatabasePath,
            new { provider = "SqlServer", connectionString = database.ConnectionString });

        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("restartRequired").GetBoolean().ShouldBeTrue();
        await _restarter.Called.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        _restarter.Calls.ShouldBe(1);

        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsFilePath, Ct))!;
        json["Urls"]!.GetValue<string>().ShouldBe("http://localhost:5123");
        json["Vyxum"]!["Agent"]!["ServerUrl"]!.GetValue<string>().ShouldBe("http://localhost:5123");
        json["Vyxum"]!["Unknown"]!["Keep"]!.GetValue<string>().ShouldBe("me");
        json["Vyxum"]!["Database"]!["Provider"]!.GetValue<string>().ShouldBe("SqlServer");
        json["Vyxum"]!["Database"]!["ConnectionString"]!.GetValue<string>().ShouldBe(database.ConnectionString);

        var current = await GetStatusAsync(client);
        current.GetProperty("restartPending").GetBoolean().ShouldBeTrue();
        current.GetProperty("database").GetProperty("provider").GetString().ShouldBe("Sqlite");
        current.ToString().ShouldNotContain(database.ConnectionString);

        // Going back to the running database before the restart undoes the change.
        var (backStatus, back) = await PostAsync(client, SetupEndpointExtensions.DatabasePath, new { provider = "Sqlite" });
        backStatus.ShouldBe(HttpStatusCode.OK);
        back.GetProperty("restartRequired").GetBoolean().ShouldBeFalse();
        (await GetStatusAsync(client)).GetProperty("restartPending").GetBoolean().ShouldBeFalse();
        json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsFilePath, Ct))!;
        json["Vyxum"]!.AsObject().ContainsKey("Database").ShouldBeFalse();
        json["Vyxum"]!["Unknown"]!["Keep"]!.GetValue<string>().ShouldBe("me");
    }

    [Fact]
    public async Task Host_reads_the_database_from_the_settings_file()
    {
        var other = Path.Combine(_dataDirectory, "elsewhere", "from-settings-file.db");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        new VyxumSettingsFile(SettingsFilePath).Update(new Dictionary<string, string?>
        {
            [DatabaseSettings.ProviderKey] = "Sqlite",
            [DatabaseSettings.ConnectionStringKey] = new SqliteConnectionStringBuilder { DataSource = other }.ToString(),
        });
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var status = await GetStatusAsync(client);

        status.GetProperty("database").GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        File.Exists(other).ShouldBeTrue();
        File.Exists(Path.Combine(_dataDirectory, DatabaseSettings.DefaultSqliteFileName)).ShouldBeFalse();
    }

    private sealed class FakeRestarter : IHostRestarter
    {
        private int _calls;

        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public bool Restart()
        {
            Interlocked.Increment(ref _calls);
            Called.TrySetResult();
            return true;
        }
    }

    /// <summary>Makes every request look as if it came from <paramref name="address"/>.</summary>
    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
