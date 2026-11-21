using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
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
    }

    [Theory]
    [InlineData(PlatformSide.AgentHubContract.Register, new[] { typeof(PlatformSide.AgentRegistration) })]
    [InlineData(PlatformSide.AgentHubContract.ReportReading, new[] { typeof(string), typeof(PlatformSide.AgentReading) })]
    [InlineData(PlatformSide.AgentHubContract.ReportStatus, new[] { typeof(string), typeof(PlatformSide.AgentDeviceStatus) })]
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
        var sent = new AgentSide.AgentRegistration("PC-1", "1.0.0", ["scale-1", "scale-2"]);

        var received = JsonSerializer.Deserialize<PlatformSide.AgentRegistration>(JsonSerializer.Serialize(sent, Json), Json);

        received.ShouldNotBeNull();
        received.AgentName.ShouldBe("PC-1");
        received.Version.ShouldBe("1.0.0");
        received.DeviceIds.ShouldBe(["scale-1", "scale-2"]);
    }

    private static PropertyInfo[] Properties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract")];

    private static bool IsNullable(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) is not null
        || new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;
}
