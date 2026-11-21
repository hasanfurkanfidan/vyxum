using Shouldly;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.Platform.Tests.Devices;

/// <summary>How the live weight store follows device settings and changing registrations (decision V-39).</summary>
public class DeviceSettingsStoreTests
{
    private readonly LiveWeightStore _store = new(new TestClock());

    private static AgentReading Reading(decimal value) => new(value, "kg", LiveWeightState.Stable, null, null, "raw", DateTimeOffset.UnixEpoch);

    [Fact]
    public void Registration_without_a_device_releases_it()
    {
        _store.RegisterAgent("c1", "PC-1", ["a", "b"]);
        _store.ReportReading("c1", "b", Reading(1m));

        _store.RegisterAgent("c1", "PC-1", ["a"]);

        _store.GetDevices().Select(d => d.DeviceId).ShouldBe(["a"]);
        _store.GetDevice("b").ShouldBeNull();
        _store.GetLatest("b").ShouldBeNull();
        _store.TryGetAgent("b", out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Registration_of_one_agent_does_not_release_the_devices_of_another()
    {
        _store.RegisterAgent("c1", "PC-1", ["a"]);
        _store.RegisterAgent("c2", "PC-2", ["b"]);

        _store.RegisterAgent("c2", "PC-2", []);

        _store.GetDevices().Select(d => d.DeviceId).ShouldBe(["a"]);
    }

    [Fact]
    public void Configured_devices_are_listed_with_their_details_before_an_agent_reports_them()
    {
        _store.SetConfiguredDevices([new ConfiguredDevice("scale-1", "Packing", 6m, 0.002m, "kg")]);

        var device = _store.GetDevice("scale-1").ShouldNotBeNull();
        device.State.ShouldBe(DeviceConnectionState.Disconnected);
        device.AgentName.ShouldBeNull();
        device.Name.ShouldBe("Packing");
        device.Capacity.ShouldBe(6m);
        device.Division.ShouldBe(0.002m);
        device.Unit.ShouldBe("kg");

        _store.RegisterAgent("c1", "PC-1", ["scale-1"]);
        _store.GetDevice("scale-1")!.Name.ShouldBe("Packing");

        // Released by its agent, it stays listed because it is configured; removed from the settings, it goes.
        _store.RegisterAgent("c1", "PC-1", []);
        _store.GetDevice("scale-1")!.State.ShouldBe(DeviceConnectionState.Disconnected);
        _store.SetConfiguredDevices([]);
        _store.GetDevices().ShouldBeEmpty();
    }

    [Fact]
    public async Task Device_watchers_see_new_details()
    {
        _store.RegisterAgent("c1", "PC-1", ["scale-1"]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var watch = _store.WatchDeviceAsync("scale-1", cts.Token).GetAsyncEnumerator(cts.Token);
        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.Name.ShouldBeNull();

        _store.SetConfiguredDevices([new ConfiguredDevice("scale-1", "Packing", null, null, null)]);

        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.Name.ShouldBe("Packing");
    }

    [Fact]
    public void Error_state_is_stored_with_its_reason()
    {
        _store.RegisterAgent("c1", "PC-1", ["scale-1"]);

        _store.ReportStatus("c1", "scale-1", new AgentDeviceStatus(DeviceConnectionState.Error, "bad protocol", null, DateTimeOffset.UnixEpoch));

        var device = _store.GetDevice("scale-1")!;
        device.State.ShouldBe(DeviceConnectionState.Error);
        device.LastError.ShouldBe("bad protocol");
    }
}

public class AgentDeviceAssignmentsTests
{
    [Fact]
    public void First_agent_to_join_is_the_default_and_the_next_one_takes_over_when_it_leaves()
    {
        var assignments = new AgentDeviceAssignments();

        assignments.Join("c1", "PC-1").ShouldBeTrue();
        assignments.Join("c2", "PC-2").ShouldBeFalse();
        assignments.Join("c1", "PC-1").ShouldBeTrue();
        assignments.Connections.ShouldBe(["c1", "c2"]);

        assignments.Leave("c2").ShouldBeNull();
        assignments.Join("c2", "PC-2").ShouldBeFalse();
        assignments.Leave("c1").ShouldBe("c2");
        assignments.Leave("c1").ShouldBeNull();
        assignments.Leave("c2").ShouldBeNull();
        assignments.Connections.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, "PC-1", true, true)]
    [InlineData(null, "PC-1", false, false)]
    [InlineData("pc-1", "PC-1", false, true)]
    [InlineData("PC-2", "PC-1", true, false)]
    public void Devices_go_to_the_named_agent_or_else_the_default_agent(string? deviceAgent, string agent, bool isDefault, bool served) =>
        AgentDeviceAssignments.Serves(deviceAgent, agent, isDefault).ShouldBe(served);
}
