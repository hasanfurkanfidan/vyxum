using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using Vyxum.Agent;
using Vyxum.Devices;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Xunit;
using AgentSide = Vyxum.Agent.Contract;
using PlatformSide = Vyxum.Platform;

namespace Vyxum.IntegrationTests;

/// <summary>
/// The agent does not reference the web application, so it has its own copy of the hub contract. These tests keep the
/// two copies in step: same names, same shapes, and what the agent writes is what the platform reads.
/// </summary>
public class AgentContractTests
{
    // The SignalR JSON protocol uses camel case by default; both sides add the string enum converter.
    private static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [Fact]
    public void Path_header_and_method_names_match()
    {
        AgentSide.AgentHubContract.Path.ShouldBe(PlatformSide.AgentHubContract.Path);
        AgentSide.AgentHubContract.KeyHeader.ShouldBe(PlatformSide.AgentHubContract.KeyHeader);
        AgentSide.AgentHubContract.Register.ShouldBe(PlatformSide.AgentHubContract.Register);
        AgentSide.AgentHubContract.ReportReading.ShouldBe(PlatformSide.AgentHubContract.ReportReading);
        AgentSide.AgentHubContract.ReportStatus.ShouldBe(PlatformSide.AgentHubContract.ReportStatus);
        AgentSide.AgentHubContract.Zero.ShouldBe(PlatformSide.AgentHubContract.Zero);
        AgentSide.AgentHubContract.GetDevices.ShouldBe(PlatformSide.AgentHubContract.GetDevices);
        AgentSide.AgentHubContract.DevicesChanged.ShouldBe(PlatformSide.AgentHubContract.DevicesChanged);
        AgentSide.AgentHubContract.Discover.ShouldBe(PlatformSide.AgentHubContract.Discover);
        AgentSide.AgentHubContract.ReportDiscoveryProgress.ShouldBe(PlatformSide.AgentHubContract.ReportDiscoveryProgress);
        AgentSide.AgentDeviceCommands.Zero.ShouldBe(DeviceCommandNames.Zero);
    }

    [Theory]
    [InlineData(PlatformSide.AgentHubContract.Register, new[] { typeof(PlatformSide.AgentRegistration) })]
    [InlineData(PlatformSide.AgentHubContract.ReportReading, new[] { typeof(string), typeof(PlatformSide.AgentReading) })]
    [InlineData(PlatformSide.AgentHubContract.ReportStatus, new[] { typeof(string), typeof(PlatformSide.AgentDeviceStatus) })]
    [InlineData(PlatformSide.AgentHubContract.ReportDiscoveryProgress, new[] { typeof(string), typeof(IReadOnlyList<PlatformSide.DiscoveryPortState>) })]
    public void Hub_methods_exist_with_the_contract_parameters(string name, Type[] parameters)
    {
        var method = typeof(AgentHub).GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
        method.ShouldNotBeNull();
        method.GetParameters().Select(p => p.ParameterType).ShouldBe(parameters);
    }

    [Theory]
    [InlineData(typeof(AgentSide.AgentRegistration), typeof(PlatformSide.AgentRegistration))]
    [InlineData(typeof(AgentSide.AgentReading), typeof(PlatformSide.AgentReading))]
    [InlineData(typeof(AgentSide.AgentDeviceStatus), typeof(PlatformSide.AgentDeviceStatus))]
    [InlineData(typeof(AgentSide.AgentCommandResult), typeof(PlatformSide.AgentCommandResult))]
    [InlineData(typeof(AgentSide.AgentDeviceConfig), typeof(PlatformSide.AgentDeviceConfig))]
    [InlineData(typeof(AgentSide.DiscoveryRequest), typeof(PlatformSide.DiscoveryRequest))]
    [InlineData(typeof(AgentSide.DiscoveryTcpTarget), typeof(PlatformSide.DiscoveryTcpTarget))]
    [InlineData(typeof(AgentSide.DiscoveryPortState), typeof(PlatformSide.DiscoveryPortState))]
    [InlineData(typeof(AgentSide.DiscoveryReading), typeof(PlatformSide.DiscoveryReading))]
    [InlineData(typeof(AgentSide.DiscoveryCandidate), typeof(PlatformSide.DiscoveryCandidate))]
    public void Dtos_have_the_same_properties(Type agentType, Type platformType)
    {
        var agentProperties = Properties(agentType);
        var platformProperties = Properties(platformType);

        agentProperties.Select(p => p.Name).ShouldBe(platformProperties.Select(p => p.Name));
        foreach (var (agent, platform) in agentProperties.Zip(platformProperties))
        {
            var agentPropertyType = Nullable.GetUnderlyingType(agent.PropertyType) ?? agent.PropertyType;
            var platformPropertyType = Nullable.GetUnderlyingType(platform.PropertyType) ?? platform.PropertyType;
            if (agentPropertyType.IsEnum)
            {
                platformPropertyType.IsEnum.ShouldBeTrue(agent.Name);
                Enum.GetNames(agentPropertyType).ShouldBe(Enum.GetNames(platformPropertyType), $"enum names of {agent.Name}");
            }
            else if (agentPropertyType.IsGenericType && agentPropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                platformPropertyType.GetGenericTypeDefinition().ShouldBe(typeof(IReadOnlyList<>), agent.Name);
                agentPropertyType.GetGenericArguments()[0].Name.ShouldBe(platformPropertyType.GetGenericArguments()[0].Name, agent.Name);
            }
            else if (agentPropertyType.Namespace == typeof(AgentSide.AgentHubContract).Namespace)
            {
                // A nested contract type: compared by its own test case.
                agentPropertyType.Name.ShouldBe(platformPropertyType.Name, agent.Name);
            }
            else
            {
                agentPropertyType.ShouldBe(platformPropertyType, agent.Name);
            }

            IsNullable(agent).ShouldBe(IsNullable(platform), $"nullability of {agent.Name}");
        }
    }

    [Fact]
    public void Enum_names_match()
    {
        Enum.GetNames<WeightState>().ShouldBe(Enum.GetNames<LiveWeightState>());
        Enum.GetNames<DeviceStatus>().ShouldBe(Enum.GetNames<DeviceConnectionState>());
        Enum.GetNames<AgentSide.AgentCommandOutcome>().ShouldBe(Enum.GetNames<DeviceCommandOutcome>());
        Enum.GetNames<AgentSide.DiscoveryPortStatus>().ShouldBe(Enum.GetNames<PlatformSide.DiscoveryPortStatus>());
    }

    [Theory]
    [InlineData(WeightState.Stable)]
    [InlineData(WeightState.Motion)]
    public void Reading_written_by_the_agent_is_read_by_the_platform(WeightState state)
    {
        var timestamp = new DateTimeOffset(2026, 11, 21, 10, 30, 0, TimeSpan.FromHours(3));
        var sent = AgentSide.AgentReading.From(new WeightReading(1.250m, "kg", state, 0.100m, true, "S S      1.250 kg", timestamp));

        var json = JsonSerializer.Serialize(sent, Json);
        var received = JsonSerializer.Deserialize<PlatformSide.AgentReading>(json, Json);

        json.ShouldContain("\"value\":1.250");
        json.ShouldContain($"\"state\":\"{state}\"");
        received.ShouldNotBeNull();
        received.Value.ShouldBe(1.250m);
        received.Value!.Value.Scale.ShouldBe((byte)3);
        received.Unit.ShouldBe("kg");
        received.State.ToString().ShouldBe(state.ToString());
        received.Tare.ShouldBe(0.100m);
        received.IsNet.ShouldBe(true);
        received.Raw.ShouldBe("S S      1.250 kg");
        received.Timestamp.ShouldBe(timestamp);
    }

    [Theory]
    [InlineData(WeightState.Overload)]
    [InlineData(WeightState.Underload)]
    public void Reading_without_value_is_read_by_the_platform(WeightState state)
    {
        var sent = AgentSide.AgentReading.From(new WeightReading(null, null, state, null, null, "S +", DateTimeOffset.UtcNow));

        var received = JsonSerializer.Deserialize<PlatformSide.AgentReading>(JsonSerializer.Serialize(sent, Json), Json);

        received.ShouldNotBeNull();
        received.Value.ShouldBeNull();
        received.Unit.ShouldBeNull();
        received.State.ToString().ShouldBe(state.ToString());
    }

    [Theory]
    [InlineData(DeviceStatus.Connecting)]
    [InlineData(DeviceStatus.Connected)]
    [InlineData(DeviceStatus.Disconnected)]
    public void Status_written_by_the_agent_is_read_by_the_platform(DeviceStatus status)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var sent = AgentSide.AgentDeviceStatus.From(new DeviceStatusInfo("scale-1", status, "port gone", TimeSpan.FromSeconds(2), timestamp));

        var received = JsonSerializer.Deserialize<PlatformSide.AgentDeviceStatus>(JsonSerializer.Serialize(sent, Json), Json);

        received.ShouldNotBeNull();
        received.Status.ToString().ShouldBe(status.ToString());
        received.LastError.ShouldBe("port gone");
        received.RetryIn.ShouldBe(TimeSpan.FromSeconds(2));
        received.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public void Registration_written_by_the_agent_is_read_by_the_platform()
    {
        var sent = new AgentSide.AgentRegistration(
            "PC-1",
            "1.0.0",
            ["scale-1", "scale-2"],
            new Dictionary<string, IReadOnlyList<string>> { ["scale-1"] = [AgentSide.AgentDeviceCommands.Zero], ["scale-2"] = [] });

        var json = JsonSerializer.Serialize(sent, Json);
        var received = JsonSerializer.Deserialize<PlatformSide.AgentRegistration>(json, Json);

        json.ShouldContain("\"deviceCommands\":{\"scale-1\":[\"Zero\"],\"scale-2\":[]}");
        received.ShouldNotBeNull();
        received.AgentName.ShouldBe("PC-1");
        received.Version.ShouldBe("1.0.0");
        received.DeviceIds.ShouldBe(["scale-1", "scale-2"]);
        received.DeviceCommands.ShouldNotBeNull();
        received.DeviceCommands["scale-1"].ShouldBe([DeviceCommandNames.Zero]);
        received.DeviceCommands["scale-2"].ShouldBeEmpty();
    }

    [Fact]
    public void Registration_from_an_agent_without_commands_is_read_by_the_platform()
    {
        var received = JsonSerializer.Deserialize<PlatformSide.AgentRegistration>(
            "{\"agentName\":\"PC-1\",\"version\":null,\"deviceIds\":[\"scale-1\"]}", Json);

        received.ShouldNotBeNull();
        received.DeviceCommands.ShouldBeNull();
    }

    [Theory]
    [InlineData(AgentSide.AgentCommandOutcome.Done, null)]
    [InlineData(AgentSide.AgentCommandOutcome.NotSupported, "no zero")]
    [InlineData(AgentSide.AgentCommandOutcome.Rejected, "Z I")]
    [InlineData(AgentSide.AgentCommandOutcome.Unavailable, "not connected")]
    public void Command_result_written_by_the_agent_is_read_by_the_platform(AgentSide.AgentCommandOutcome outcome, string? detail)
    {
        var json = JsonSerializer.Serialize(new AgentSide.AgentCommandResult(outcome, detail), Json);
        var received = JsonSerializer.Deserialize<PlatformSide.AgentCommandResult>(json, Json);

        json.ShouldContain($"\"result\":\"{outcome}\"");
        received.ShouldNotBeNull();
        received.Result.ToString().ShouldBe(outcome.ToString());
        received.Detail.ShouldBe(detail);
    }

    [Fact]
    public void GetDevices_hub_method_returns_the_device_list()
    {
        var method = typeof(AgentHub).GetMethod(PlatformSide.AgentHubContract.GetDevices, BindingFlags.Public | BindingFlags.Instance);
        method.ShouldNotBeNull();
        method.GetParameters().ShouldBeEmpty();
        method.ReturnType.ShouldBe(typeof(Task<IReadOnlyList<PlatformSide.AgentDeviceConfig>>));
    }

    [Fact]
    public void Device_sent_by_the_platform_is_read_by_the_agent()
    {
        using var transport = JsonDocument.Parse("""{"type":"serial","portName":"COM3","baudRate":"19200","parity":"Even","dataBits":7}""");
        using var protocol = JsonDocument.Parse("""{"type":"mt-sics","command":"SI","pollInterval":"00:00:00.200"}""");
        var sent = new PlatformSide.AgentDeviceConfig("scale-1", transport.RootElement, protocol.RootElement);

        var json = JsonSerializer.Serialize(new[] { sent }, Json);
        var received = JsonSerializer.Deserialize<AgentSide.AgentDeviceConfig[]>(json, Json).ShouldNotBeNull().ShouldHaveSingleItem();
        var errors = new List<string>();
        var settings = Vyxum.Agent.Configuration.AgentConfiguration.ReadServerDevice(received, "Device scale-1", errors);

        errors.ShouldBeEmpty();
        settings.ShouldNotBeNull();
        settings.Id.ShouldBe("scale-1");
        settings.Transport!.PortName.ShouldBe("COM3");
        settings.Transport.BaudRate.ShouldBe(19200);
        settings.Transport.DataBits.ShouldBe(7);
        settings.Transport.Parity.ShouldBe(Vyxum.Devices.Transports.SerialParity.Even);
        settings.Protocol!.PollInterval.ShouldBe(TimeSpan.FromMilliseconds(200));
        var device = Vyxum.Agent.Configuration.AgentConfiguration.CreateDevice(settings, "Device scale-1", errors);
        errors.ShouldBeEmpty();
        device.ShouldNotBeNull().PortName.ShouldBe("COM3");
    }

    [Fact]
    public void Device_with_a_value_of_the_wrong_type_is_reported_not_thrown()
    {
        using var transport = JsonDocument.Parse("""{"type":"serial","portName":"COM3","baudRate":"fast"}""");
        using var protocol = JsonDocument.Parse("""{"type":"mt-sics"}""");
        var errors = new List<string>();

        var settings = Vyxum.Agent.Configuration.AgentConfiguration.ReadServerDevice(
            new AgentSide.AgentDeviceConfig("scale-1", transport.RootElement.Clone(), protocol.RootElement.Clone()), "Device scale-1", errors);

        settings.ShouldBeNull();
        errors.ShouldHaveSingleItem().ShouldStartWith("Device scale-1:");
    }

    [Fact]
    public void Zero_client_method_has_the_contract_signature_on_the_agent()
    {
        var method = typeof(DeviceFleet).GetMethod(nameof(DeviceFleet.ZeroAsync));
        method.ShouldNotBeNull();
        method.GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(string), typeof(TimeSpan), typeof(CancellationToken)]);
        method.ReturnType.ShouldBe(typeof(Task<AgentSide.AgentCommandResult>));
    }

    [Fact]
    public void Discovery_result_has_the_same_properties_on_both_sides()
    {
        Properties(typeof(AgentSide.DiscoveryResult)).Select(p => p.Name)
            .ShouldBe(Properties(typeof(PlatformSide.DiscoveryResult)).Select(p => p.Name));
    }

    [Fact]
    public void Discovery_request_written_by_the_platform_is_read_by_the_agent()
    {
        var json = JsonSerializer.Serialize(new PlatformSide.DiscoveryRequest("job-1", new PlatformSide.DiscoveryTcpTarget("10.0.0.5", 4001)), Json);
        var received = JsonSerializer.Deserialize<AgentSide.DiscoveryRequest>(json, Json);

        received.ShouldNotBeNull();
        received.DiscoveryId.ShouldBe("job-1");
        received.Tcp.ShouldBe(new AgentSide.DiscoveryTcpTarget("10.0.0.5", 4001));
        JsonSerializer.Deserialize<AgentSide.DiscoveryRequest>("{\"discoveryId\":\"job-2\",\"tcp\":null}", Json)!.Tcp.ShouldBeNull();
    }

    [Fact]
    public void Discovery_result_written_by_the_agent_is_read_by_the_platform()
    {
        using var transport = JsonDocument.Parse("""{"type":"serial","portName":"COM3","baudRate":9600}""");
        using var protocol = JsonDocument.Parse("""{"type":"mt-sics","command":"SI"}""");
        var sent = new AgentSide.DiscoveryResult(
            [new AgentSide.DiscoveryPortState("COM3", AgentSide.DiscoveryPortStatus.Found, null), new AgentSide.DiscoveryPortState("COM4", AgentSide.DiscoveryPortStatus.InUse, "used")],
            [new AgentSide.DiscoveryCandidate(
                "serial:COM3", "COM3", null, null, transport.RootElement, protocol.RootElement, "C139673552",
                new AgentSide.DiscoveryReading("572", "g", WeightState.Stable), "MT-SICS, 9600 8N1")]);

        var json = JsonSerializer.Serialize(sent, Json);
        var received = JsonSerializer.Deserialize<PlatformSide.DiscoveryResult>(json, Json);

        received.ShouldNotBeNull();
        received.Ports.Select(p => (p.PortName, p.State.ToString(), p.Detail)).ShouldBe([("COM3", "Found", null), ("COM4", "InUse", "used")]);
        var candidate = received.Candidates.ShouldHaveSingleItem();
        candidate.Key.ShouldBe("serial:COM3");
        candidate.Transport.GetProperty("portName").GetString().ShouldBe("COM3");
        candidate.Protocol.GetProperty("command").GetString().ShouldBe("SI");
        candidate.SerialNumber.ShouldBe("C139673552");
        candidate.Reading.ShouldBe(new PlatformSide.DiscoveryReading("572", "g", LiveWeightState.Stable));
        candidate.Description.ShouldBe("MT-SICS, 9600 8N1");
    }

    [Fact]
    public void Discover_client_method_has_the_contract_signature_on_the_agent()
    {
        var method = typeof(Vyxum.Agent.Discovery.DeviceDiscovery).GetMethod(nameof(Vyxum.Agent.Discovery.DeviceDiscovery.DiscoverAsync));
        method.ShouldNotBeNull();
        method.GetParameters()[0].ParameterType.ShouldBe(typeof(AgentSide.DiscoveryRequest));
        method.ReturnType.ShouldBe(typeof(Task<AgentSide.DiscoveryResult>));
    }

    private static PropertyInfo[] Properties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract")];

    private static bool IsNullable(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) is not null
        || new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;
}
