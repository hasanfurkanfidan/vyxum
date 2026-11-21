using Microsoft.Extensions.Configuration;
using Shouldly;
using Vyxum.Agent;
using Vyxum.Agent.Configuration;
using Vyxum.Devices.MtSics;
using Xunit;
using static Vyxum.IntegrationTests.AgentTestSupport;

namespace Vyxum.IntegrationTests;

public class AgentConfigurationTests
{
    private const string Device = "Vyxum:Agent:Devices:0:";

    private static Dictionary<string, string?> Valid() =>
        AgentConfig("http://localhost:5000", key: null, new SimulatedDevice("scale-1", 572m, "g"));

    private static Dictionary<string, string?> Serial() => new()
    {
        [Device + "Id"] = "ind231",
        [Device + "Transport:Type"] = "serial",
        [Device + "Transport:PortName"] = "COM3",
        [Device + "Protocol:Type"] = "mt-sics",
        [Device + "Protocol:Command"] = "SI",
    };

    private static IReadOnlyList<AgentDevice> Create(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return AgentConfiguration.CreateDevices(AgentConfiguration.Bind(configuration));
    }

    private static AgentConfigurationException Invalid(Dictionary<string, string?> values) =>
        Should.Throw<AgentConfigurationException>(() => Create(values));

    [Fact]
    public void Valid_simulator_settings_create_a_device()
    {
        var device = Create(Valid()).ShouldHaveSingleItem();

        device.Id.ShouldBe("scale-1");
        device.ProtocolName.ShouldBe("mt-sics");
        device.Simulator.ShouldNotBeNull();
        device.Protocol.ShouldBeOfType<MtSicsProtocol>();
    }

    [Fact]
    public void Serial_settings_create_a_device_without_opening_the_port()
    {
        var device = Create(Serial()).ShouldHaveSingleItem();

        device.TransportDescription.ShouldBe("COM3 9600 8N1");
        device.Simulator.ShouldBeNull();
    }

    [Theory]
    [InlineData("appsettings.Development.json", "simulator-1")]
    [InlineData("appsettings.Ind231.example.json", "ind231")]
    public void Shipped_settings_files_are_valid(string file, string deviceId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        File.Exists(path).ShouldBeTrue($"{file} should be copied next to the agent");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddJsonFile(path)
            .Build();

        var settings = AgentConfiguration.Bind(configuration);
        var device = AgentConfiguration.CreateDevices(settings).ShouldHaveSingleItem();

        device.Id.ShouldBe(deviceId);
        settings.ServerUrl.ShouldBe(AgentSettings.DefaultServerUrl);
    }

    [Fact]
    public void Installed_settings_file_lists_no_devices()
    {
        // An installed agent gets its devices from the web application (decision V-39).
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build();

        var settings = AgentConfiguration.Bind(configuration);

        settings.Devices.ShouldBeEmpty();
        AgentConfiguration.CreateDevices(settings).ShouldBeEmpty();
        settings.ServerUrl.ShouldBe(AgentSettings.DefaultServerUrl);
    }

    [Fact]
    public void Unknown_transport_type_is_rejected()
    {
        var values = Valid();
        values[Device + "Transport:Type"] = "usb";

        var error = Invalid(values).Errors.ShouldHaveSingleItem();

        error.ShouldStartWith(Device + "Transport:Type:");
        error.ShouldContain("'usb'");
        error.ShouldContain("serial, tcp, simulator");
    }

    [Fact]
    public void Serial_transport_requires_a_port_name()
    {
        var values = Serial();
        values.Remove(Device + "Transport:PortName");

        Invalid(values).Errors.ShouldHaveSingleItem().ShouldStartWith(Device + "Transport:PortName:");
    }

    [Fact]
    public void Tcp_transport_requires_host_and_port()
    {
        var values = Serial();
        values[Device + "Transport:Type"] = "tcp";

        var errors = Invalid(values).Errors;

        errors.ShouldContain(e => e.StartsWith(Device + "Transport:Host:", StringComparison.Ordinal));
        errors.ShouldContain(e => e.StartsWith(Device + "Transport:Port:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("T")]
    [InlineData("Z")]
    [InlineData("@")]
    [InlineData("TI")]
    [InlineData("M01 1")]
    public void Mt_sics_commands_that_change_the_scale_are_rejected(string command)
    {
        var values = Valid();
        values[Device + "Protocol:Command"] = command;

        var error = Invalid(values).Errors.ShouldHaveSingleItem();

        error.ShouldStartWith(Device + "Protocol:Command:");
        error.ShouldContain("not allowed");
        error.ShouldContain("S, SI, SIR");
    }

    [Fact]
    public void Mt_sics_zero_timeout_must_be_positive_and_zero_support_is_per_protocol()
    {
        var values = Valid();
        values[Device + "Protocol:ZeroTimeout"] = "00:00:00";

        Invalid(values).Errors.ShouldHaveSingleItem().ShouldStartWith(Device + "Protocol:ZeroTimeout:");

        values[Device + "Protocol:ZeroTimeout"] = "00:00:02";
        var device = Create(values).Single();
        device.Protocol.ShouldBeAssignableTo<Vyxum.Devices.ISupportsZero>();
    }

    [Fact]
    public void Unknown_protocol_type_is_rejected()
    {
        var values = Valid();
        values[Device + "Protocol:Type"] = "modbus";

        Invalid(values).Errors.ShouldHaveSingleItem().ShouldStartWith(Device + "Protocol:Type:");
    }

    [Fact]
    public void Generic_ascii_requires_a_pattern()
    {
        var values = Valid();
        values[Device + "Protocol:Type"] = "generic-ascii";

        Invalid(values).Errors.ShouldHaveSingleItem().ShouldStartWith(Device + "Protocol:Pattern:");
    }

    [Fact]
    public void Value_of_the_wrong_type_names_the_setting()
    {
        var values = Serial();
        values[Device + "Transport:Parity"] = "Sometimes";

        var exception = Invalid(values);

        exception.Message.ShouldContain(Device + "Transport:Parity");
    }

    [Fact]
    public void No_devices_is_valid_but_duplicate_ids_and_bad_server_url_are_reported()
    {
        Create(new Dictionary<string, string?>()).ShouldBeEmpty();

        var empty = new Dictionary<string, string?> { ["Vyxum:Agent:ServerUrl"] = "localhost" };
        Invalid(empty).Errors.ShouldHaveSingleItem().ShouldStartWith("Vyxum:Agent:ServerUrl:");

        var duplicate = Valid();
        foreach (var (key, value) in Valid())
        {
            if (key.StartsWith(Device, StringComparison.Ordinal))
            {
                duplicate[key.Replace(":Devices:0:", ":Devices:1:", StringComparison.Ordinal)] = value;
            }
        }

        Invalid(duplicate).Errors.ShouldHaveSingleItem().ShouldStartWith("Vyxum:Agent:Devices:1:Id:");
    }

    [Fact]
    public async Task Agent_host_does_not_start_with_invalid_settings()
    {
        var values = Valid();
        values[Device + "Protocol:Command"] = "T";
        using var host = BuildAgent(values);

        var exception = await Should.ThrowAsync<AgentConfigurationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(Device + "Protocol:Command");
    }

    [Fact]
    public void Key_is_not_part_of_any_error_message()
    {
        var values = Valid();
        values["Vyxum:Agent:Key"] = "super-secret-key";
        values[Device + "Transport:Type"] = "usb";

        Invalid(values).Message.ShouldNotContain("super-secret-key");
    }
}
