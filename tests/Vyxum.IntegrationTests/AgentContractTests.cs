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
        AgentSide.AgentDeviceCommands.Zero.ShouldBe(DeviceCommandNames.Zero);
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
    [InlineData(typeof(AgentSide.AgentCommandResult), typeof(PlatformSide.AgentCommandResult))]
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
        Enum.GetNames<AgentSide.AgentCommandOutcome>().ShouldBe(Enum.GetNames<DeviceCommandOutcome>());
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
    public void Zero_client_method_has_the_contract_signature_on_the_agent()
    {
        var method = typeof(DeviceFleet).GetMethod(nameof(DeviceFleet.ZeroAsync));
        method.ShouldNotBeNull();
        method.GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(string), typeof(TimeSpan), typeof(CancellationToken)]);
        method.ReturnType.ShouldBe(typeof(Task<AgentSide.AgentCommandResult>));
    }

    private static PropertyInfo[] Properties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract")];

    private static bool IsNullable(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) is not null
        || new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;
}
