using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>The device settings API, <c>/api/devices/config</c> (decision V-39), on both providers.</summary>
public sealed class DeviceConfigApiTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "vyxum-devcfg-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private WebApplicationFactory<Program> CreateFactory(TestDatabase? database) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DatabaseSettings.DataDirectoryKey, _dataDirectory);
            if (database is not null)
            {
                builder.UseSetting(DatabaseSettings.ProviderKey, database.Provider.ToString());
                builder.UseSetting(DatabaseSettings.ConnectionStringKey, database.ConnectionString);
            }
        });

    private static async Task<TestDatabase?> DatabaseAsync(DatabaseProvider provider) =>
        await Task.FromResult(provider == DatabaseProvider.SqlServer ? Databases.CreateOrSkip(provider) : null);

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpClient client, HttpMethod method, string path, string? json = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = Body(json);
        }

        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(text.Length == 0 ? "null" : text);
        return (response.StatusCode, document.RootElement.Clone());
    }

    private const string SerialScale = """
        {
          "id": "scale-1",
          "name": "Packing scale",
          "transport": { "type": "serial", "portName": "COM3", "baudRate": 9600, "parity": "None" },
          "protocol": { "type": "mt-sics", "command": "SI" },
          "capacity": "6",
          "division": "0.002",
          "unit": "kg"
        }
        """;

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Devices_are_created_read_changed_and_deleted(DatabaseProvider provider)
    {
        await using var database = await DatabaseAsync(provider);
        await using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        (await SendAsync(client, HttpMethod.Get, DeviceEndpointExtensions.ConfigPath)).Body.GetArrayLength().ShouldBe(0);

        using (var created = await client.PostAsync(DeviceEndpointExtensions.ConfigPath, Body(SerialScale), Ct))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            created.Headers.Location!.ToString().ShouldBe("/api/devices/config/scale-1");
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
            var device = json.RootElement;
            device.GetProperty("id").GetString().ShouldBe("scale-1");
            device.GetProperty("name").GetString().ShouldBe("Packing scale");
            device.GetProperty("agentName").ValueKind.ShouldBe(JsonValueKind.Null);
            device.GetProperty("transport").GetProperty("portName").GetString().ShouldBe("COM3");
            device.GetProperty("protocol").GetProperty("type").GetString().ShouldBe("mt-sics");
            device.GetProperty("capacity").GetString().ShouldBe("6");
            device.GetProperty("division").GetString().ShouldBe("0.002");
            device.GetProperty("unit").GetString().ShouldBe("kg");
            device.GetProperty("isEnabled").GetBoolean().ShouldBeTrue();
        }

        // Decimals come back exactly, also from SQL Server, which pads them to the column scale.
        var (status, read) = await SendAsync(client, HttpMethod.Get, DeviceEndpointExtensions.ConfigPath + "/scale-1");
        status.ShouldBe(HttpStatusCode.OK);
        read.GetProperty("division").GetString().ShouldBe("0.002");
        read.GetProperty("transport").GetProperty("baudRate").GetInt32().ShouldBe(9600);

        // The live device list shows the configured device before any agent reports it.
        var live = factory.Services.GetRequiredService<ILiveWeightSource>().GetDevice("scale-1");
        live.ShouldNotBeNull();
        live.State.ShouldBe(DeviceConnectionState.Disconnected);
        live.Name.ShouldBe("Packing scale");
        live.Capacity.ShouldBe(6m);
        live.Division.ShouldBe(0.002m);
        live.Unit.ShouldBe("kg");
        var list = await client.GetStringAsync(DeviceEndpointExtensions.DevicesPath, Ct);
        list.ShouldContain("\"name\":\"Packing scale\"");

        (status, var changed) = await SendAsync(client, HttpMethod.Put, DeviceEndpointExtensions.ConfigPath + "/scale-1", """
            {
              "id": "ignored",
              "name": "",
              "agentName": "PC-2",
              "transport": { "type": "tcp", "host": "192.168.1.20", "port": 4001 },
              "protocol": { "type": "toledo-continuous" },
              "capacity": "",
              "isEnabled": false
            }
            """);
        status.ShouldBe(HttpStatusCode.OK);
        changed.GetProperty("id").GetString().ShouldBe("scale-1");
        changed.GetProperty("name").GetString().ShouldBe("scale-1");
        changed.GetProperty("agentName").GetString().ShouldBe("PC-2");
        changed.GetProperty("transport").GetProperty("host").GetString().ShouldBe("192.168.1.20");
        changed.GetProperty("capacity").ValueKind.ShouldBe(JsonValueKind.Null);
        changed.GetProperty("isEnabled").GetBoolean().ShouldBeFalse();
        changed.GetProperty("updatedAt").GetDateTime().ShouldBeGreaterThanOrEqualTo(changed.GetProperty("createdAt").GetDateTime());

        // A disabled device is not listed as a live device.
        factory.Services.GetRequiredService<ILiveWeightSource>().GetDevice("scale-1").ShouldBeNull();

        (await SendAsync(client, HttpMethod.Delete, DeviceEndpointExtensions.ConfigPath + "/scale-1")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(client, HttpMethod.Get, DeviceEndpointExtensions.ConfigPath + "/scale-1")).Status.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(client, HttpMethod.Delete, DeviceEndpointExtensions.ConfigPath + "/scale-1")).Status.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(client, HttpMethod.Put, DeviceEndpointExtensions.ConfigPath + "/scale-1", SerialScale)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Ids_are_unique_and_the_list_is_ordered_by_id(DatabaseProvider provider)
    {
        await using var database = await DatabaseAsync(provider);
        await using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        foreach (var id in new[] { "scale-b", "scale-a" })
        {
            (await SendAsync(client, HttpMethod.Post, DeviceEndpointExtensions.ConfigPath, Simulator(id))).Status.ShouldBe(HttpStatusCode.Created);
        }

        var (status, body) = await SendAsync(client, HttpMethod.Post, DeviceEndpointExtensions.ConfigPath, Simulator("scale-a"));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("errors").GetProperty("id")[0].GetString().ShouldBe("devices.IdTaken");

        var list = (await SendAsync(client, HttpMethod.Get, DeviceEndpointExtensions.ConfigPath)).Body;
        list.EnumerateArray().Select(d => d.GetProperty("id").GetString()).ShouldBe(["scale-a", "scale-b"]);
    }

    [Theory]
    [InlineData("""{ "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" } }""", "id", "devices.IdRequired")]
    [InlineData("""{ "id": "Scale 1", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" } }""", "id", "devices.IdInvalid")]
    [InlineData("""{ "id": "config", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" } }""", "id", "devices.IdInvalid")]
    [InlineData("""{ "id": "scale-1", "protocol": { "type": "mt-sics" } }""", "transport", "devices.TransportRequired")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "usb" }, "protocol": { "type": "mt-sics" } }""", "transport.type", "devices.TransportTypeInvalid")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "serial" }, "protocol": { "type": "mt-sics" } }""", "transport.portName", "devices.PortNameRequired")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "tcp", "port": 4001 }, "protocol": { "type": "mt-sics" } }""", "transport.host", "devices.HostRequired")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "tcp", "host": "h", "port": 70000 }, "protocol": { "type": "mt-sics" } }""", "transport.port", "devices.PortInvalid")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" } }""", "protocol", "devices.ProtocolRequired")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": " " } }""", "protocol.type", "devices.ProtocolRequired")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" }, "capacity": "0" }""", "capacity", "devices.MustBePositive")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" }, "capacity": "6,5" }""", "capacity", "devices.InvalidNumber")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" }, "division": "0.0000001" }""", "division", "devices.InvalidNumber")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" }, "division": "-1" }""", "division", "devices.MustBePositive")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" }, "capacity": "6", "division": "10" }""", "division", "devices.DivisionAboveCapacity")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics" }, "unit": "kilograms-per-unit" }""", "unit", "devices.UnitTooLong")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "generic-ascii", "pattern": "x", "requestCommand": "T" } }""", "protocol.requestCommand", "devices.ReadCommandOnly")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "Generic-ASCII", "pattern": "x", "RequestCommand": "Z" } }""", "protocol.requestCommand", "devices.ReadCommandOnly")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "generic-ascii", "pattern": "x", "requestCommand": 7 } }""", "protocol.requestCommand", "devices.ReadCommandOnly")]
    [InlineData("""{ "id": "scale-1", "transport": { "type": "simulator" }, "protocol": { "type": "mt-sics", "command": "T" } }""", "protocol.command", "devices.ReadCommandOnly")]
    public async Task Invalid_input_is_a_validation_problem_with_ui_string_keys(string json, string field, string key)
    {
        await using var factory = CreateFactory(database: null);
        using var client = factory.CreateClient();

        var (status, body) = await SendAsync(client, HttpMethod.Post, DeviceEndpointExtensions.ConfigPath, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("errors").GetProperty(field)[0].GetString().ShouldBe(key);

        // Every key is a UI string in both languages.
        var en = await client.GetFromJsonAsync<JsonElement>("/api/ui/strings?culture=en", Ct);
        var tr = await client.GetFromJsonAsync<JsonElement>("/api/ui/strings?culture=tr", Ct);
        en.GetProperty("strings").TryGetProperty(key, out _).ShouldBeTrue(key);
        tr.GetProperty("strings").TryGetProperty(key, out _).ShouldBeTrue(key);
    }

    [Theory]
    [InlineData("""{ "type": "generic-ascii", "pattern": "x", "requestCommand": "SI" }""")]
    [InlineData("""{ "type": "generic-ascii", "pattern": "x", "requestCommand": "\u0005" }""")]
    [InlineData("""{ "type": "generic-ascii", "pattern": "x", "requestCommand": "" }""")]
    [InlineData("""{ "type": "generic-ascii", "pattern": "x", "requestCommand": null }""")]
    [InlineData("""{ "type": "generic-ascii", "pattern": "x" }""")]
    [InlineData("""{ "type": "mt-sics", "command": "SIR" }""")]
    public async Task Read_commands_and_listen_only_mode_are_accepted(string protocol)
    {
        await using var factory = CreateFactory(database: null);
        using var client = factory.CreateClient();

        var (status, body) = await SendAsync(client, HttpMethod.Post, DeviceEndpointExtensions.ConfigPath,
            "{ \"id\": \"scale-1\", \"transport\": { \"type\": \"simulator\" }, \"protocol\": " + protocol + " }");

        status.ShouldBe(HttpStatusCode.Created, body.ToString());
    }

    [Fact]
    public async Task Port_given_as_text_and_any_letter_case_of_the_type_are_accepted()
    {
        await using var factory = CreateFactory(database: null);
        using var client = factory.CreateClient();

        var (status, body) = await SendAsync(client, HttpMethod.Post, DeviceEndpointExtensions.ConfigPath, """
            { "id": "net-1", "transport": { "Type": "TCP", "host": "10.0.0.5", "port": "4001" }, "protocol": { "type": "generic-ascii", "pattern": "(?<weight>[0-9.]+)" } }
            """);

        status.ShouldBe(HttpStatusCode.Created);
        body.GetProperty("transport").GetProperty("port").GetString().ShouldBe("4001");
    }

    internal static string Simulator(string id, decimal weight = 572m, string unit = "g") => $$"""
        {
          "id": "{{id}}",
          "transport": { "type": "simulator", "simulator": { "kind": "MtSics", "weight": {{weight.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "unit": "{{unit}}" } },
          "protocol": { "type": "mt-sics", "command": "SI", "pollInterval": "00:00:00.050", "responseTimeout": "00:00:00.500" }
        }
        """;
}
