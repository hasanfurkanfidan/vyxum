using Shouldly;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Xunit;

namespace Vyxum.Platform.Tests.Devices;

public class LiveWeightStoreTests
{
    private readonly TestClock _clock = new();
    private readonly LiveWeightStore _store;

    public LiveWeightStoreTests()
    {
        _store = new LiveWeightStore(_clock);
    }

    private static AgentReading Reading(decimal? value, LiveWeightState state = LiveWeightState.Stable) =>
        new(value, "kg", state, null, null, "raw", DateTimeOffset.UnixEpoch);

    private static AgentDeviceStatus Status(DeviceConnectionState state, string? error = null) =>
        new(state, error, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Registered_devices_start_connecting_with_agent_name()
    {
        _store.RegisterAgent("c1", "PC-1", ["b", "a"]);

        var devices = _store.GetDevices();

        devices.Select(d => d.DeviceId).ShouldBe(["a", "b"]);
        devices.ShouldAllBe(d => d.State == DeviceConnectionState.Connecting && d.AgentName == "PC-1" && d.LastWeight == null);
    }

    [Fact]
    public void Supported_commands_come_from_the_registration_and_default_to_none()
    {
        _store.RegisterAgent("c1", "PC-1", ["a", "b"], new Dictionary<string, IReadOnlyList<string>> { ["a"] = [DeviceCommandNames.Zero] });

        _store.GetDevice("a")!.SupportedCommands.ShouldBe([DeviceCommandNames.Zero]);
        _store.GetDevice("b")!.SupportedCommands.ShouldBeEmpty();
        _store.TryGetAgent("a", out var connectionId, out var commands).ShouldBeTrue();
        connectionId.ShouldBe("c1");
        commands.ShouldBe([DeviceCommandNames.Zero]);
    }

    [Fact]
    public void No_agent_is_found_for_unknown_or_disconnected_devices()
    {
        _store.RegisterAgent("c1", "PC-1", ["a"], new Dictionary<string, IReadOnlyList<string>> { ["a"] = [DeviceCommandNames.Zero] });

        _store.TryGetAgent("unknown", out _, out _).ShouldBeFalse();
        _store.DisconnectAgent("c1");
        _store.TryGetAgent("a", out _, out var commands).ShouldBeFalse();
        commands.ShouldBeEmpty();
        _store.GetDevice("a")!.SupportedCommands.ShouldBe([DeviceCommandNames.Zero]);
    }

    [Fact]
    public async Task Registering_again_with_the_same_commands_publishes_no_change()
    {
        var commands = new Dictionary<string, IReadOnlyList<string>> { ["s"] = [DeviceCommandNames.Zero] };
        _store.RegisterAgent("c1", "PC-1", ["s"], commands);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var watch = _store.WatchDeviceAsync("s", cts.Token).GetAsyncEnumerator(cts.Token);
        (await watch.MoveNextAsync()).ShouldBeTrue();

        _store.RegisterAgent("c1", "PC-1", ["s"], new Dictionary<string, IReadOnlyList<string>> { ["s"] = [DeviceCommandNames.Zero] });
        _store.ReportStatus("c1", "s", Status(DeviceConnectionState.Connected));

        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.State.ShouldBe(DeviceConnectionState.Connected);
        watch.Current.SupportedCommands.ShouldBe([DeviceCommandNames.Zero]);
    }

    [Fact]
    public void Reading_is_stored_with_receive_time_and_marks_device_connected()
    {
        _store.RegisterAgent("c1", "PC-1", ["s"]);
        _store.ReportStatus("c1", "s", Status(DeviceConnectionState.Disconnected, "port closed"));

        var before = _clock.GetUtcNow();
        var weight = _store.ReportReading("c1", "s", Reading(1.250m));

        weight.ShouldBe(new LiveWeight("s", 1.250m, "kg", LiveWeightState.Stable, null, null, DateTimeOffset.UnixEpoch, weight.ReceivedAt));
        weight.ReceivedAt.ShouldBeInRange(before, _clock.GetUtcNow());
        _store.GetLatest("s").ShouldBe(weight);
        var device = _store.GetDevice("s").ShouldNotBeNull();
        device.State.ShouldBe(DeviceConnectionState.Connected);
        device.LastError.ShouldBeNull();
        device.LastSeen.ShouldBe(weight.ReceivedAt);
    }

    [Fact]
    public void Status_is_stored_and_keeps_last_weight()
    {
        _store.ReportReading("c1", "s", Reading(2m));

        _store.ReportStatus("c1", "s", Status(DeviceConnectionState.Disconnected, "timeout"));

        var device = _store.GetDevice("s").ShouldNotBeNull();
        device.State.ShouldBe(DeviceConnectionState.Disconnected);
        device.LastError.ShouldBe("timeout");
        device.LastWeight.ShouldNotBeNull().Value.ShouldBe(2m);
    }

    [Fact]
    public void Agent_disconnect_marks_only_its_own_devices()
    {
        _store.RegisterAgent("c1", "PC-1", ["a"]);
        _store.RegisterAgent("c2", "PC-2", ["b"]);
        _store.ReportReading("c1", "a", Reading(1m));
        _store.ReportReading("c2", "b", Reading(1m));

        _store.DisconnectAgent("c1").ShouldBe(1);

        _store.GetDevice("a").ShouldNotBeNull().State.ShouldBe(DeviceConnectionState.Disconnected);
        _store.GetDevice("b").ShouldNotBeNull().State.ShouldBe(DeviceConnectionState.Connected);
        _store.GetLatest("a").ShouldNotBeNull();
    }

    [Fact]
    public void Device_taken_over_by_new_connection_is_not_disconnected_by_old_one()
    {
        _store.RegisterAgent("old", "PC-1", ["s"]);
        _store.RegisterAgent("new", "PC-1", ["s"]);
        _store.ReportReading("new", "s", Reading(1m));

        _store.DisconnectAgent("old").ShouldBe(0);

        _store.GetDevice("s").ShouldNotBeNull().State.ShouldBe(DeviceConnectionState.Connected);
    }

    [Fact]
    public void A_second_agent_reporting_the_same_device_id_does_not_take_the_device()
    {
        _store.RegisterAgent("c1", "PC-1", ["s"]);
        _store.ReportReading("c1", "s", Reading(1m));

        var conflicts = _store.RegisterAgent("c2", "PC-2", ["s", "t"]);

        conflicts.ShouldBe([("s", "PC-1")]);
        _store.Serves("c1", "s").ShouldBeTrue();
        _store.Serves("c2", "s").ShouldBeFalse();
        _store.Serves("c2", "t").ShouldBeTrue();
        var device = _store.GetDevice("s").ShouldNotBeNull();
        device.AgentName.ShouldBe("PC-1");
        device.LastWeight.ShouldNotBeNull().Value.ShouldBe(1m);
    }

    [Fact]
    public void A_waiting_agent_gets_the_device_when_the_holder_disconnects()
    {
        _store.RegisterAgent("c1", "PC-1", ["s"]);
        _store.ReportReading("c1", "s", Reading(1m));
        _store.RegisterAgent("c2", "PC-2", ["s"]);

        _store.DisconnectAgent("c1");

        _store.Serves("c2", "s").ShouldBeTrue();
        var device = _store.GetDevice("s").ShouldNotBeNull();
        device.AgentName.ShouldBe("PC-2");
        device.State.ShouldBe(DeviceConnectionState.Connecting);
        device.LastWeight.ShouldBeNull();
    }

    [Fact]
    public void Only_the_connection_that_registered_a_device_serves_it()
    {
        _store.Serves("c1", "s").ShouldBeFalse();
        _store.RegisterAgent("c1", "PC-1", ["s"]);
        _store.Serves("c1", "s").ShouldBeTrue();
        _store.Serves("c9", "s").ShouldBeFalse();

        _store.DisconnectAgent("c1");
        _store.Serves("c1", "s").ShouldBeFalse();
    }

    [Fact]
    public void Reregistering_after_disconnect_returns_to_connecting()
    {
        _store.RegisterAgent("c1", "PC-1", ["s"]);
        _store.DisconnectAgent("c1");

        _store.RegisterAgent("c2", "PC-1", ["s"]);

        _store.GetDevice("s").ShouldNotBeNull().State.ShouldBe(DeviceConnectionState.Connecting);
    }

    [Fact]
    public void Unknown_device_has_no_snapshot()
    {
        _store.GetDevice("x").ShouldBeNull();
        _store.GetLatest("x").ShouldBeNull();
        _store.GetDevices().ShouldBeEmpty();
    }

    [Fact]
    public async Task Watch_yields_latest_then_new_readings_and_does_not_create_a_device()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.ReportReading("c1", "s", Reading(1m));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var watch = _store.WatchAsync("s", cancel.Token).GetAsyncEnumerator(cancel.Token);
        await using var unknown = _store.WatchAsync("other", cancel.Token).GetAsyncEnumerator(cancel.Token);
        var unknownMove = unknown.MoveNextAsync();

        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.Value.ShouldBe(1m);

        _store.ReportReading("c1", "s", Reading(2m, LiveWeightState.Motion));
        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.Value.ShouldBe(2m);
        watch.Current.State.ShouldBe(LiveWeightState.Motion);

        _store.GetDevices().Select(d => d.DeviceId).ShouldBe(["s"]);

        await cancel.CancelAsync();
        (await watch.MoveNextAsync()).ShouldBeFalse();
        (await unknownMove).ShouldBeFalse();
    }

    [Fact]
    public async Task Device_watch_starts_with_a_waiting_snapshot_for_an_unknown_device()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var watch = _store.WatchDeviceAsync("s", ct).GetAsyncEnumerator(ct);

        (await watch.MoveNextAsync()).ShouldBeTrue();

        watch.Current.ShouldBe(new LiveDevice("s", null, DeviceConnectionState.Disconnected, null, null, default));
        _store.GetDevice("s").ShouldBeNull();
    }

    [Fact]
    public async Task Device_watch_reports_state_and_weight_changes_but_not_identical_readings()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.RegisterAgent("c1", "PC-1", ["s"]);
        await using var watch = _store.WatchDeviceAsync("s", ct).GetAsyncEnumerator(ct);
        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.State.ShouldBe(DeviceConnectionState.Connecting);

        _store.ReportStatus("c1", "s", Status(DeviceConnectionState.Connecting));
        _store.ReportStatus("c1", "s", Status(DeviceConnectionState.Disconnected, "port closed"));
        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.State.ShouldBe(DeviceConnectionState.Disconnected);
        watch.Current.LastError.ShouldBe("port closed");

        _store.ReportReading("c1", "s", Reading(1m));
        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.State.ShouldBe(DeviceConnectionState.Connected);
        watch.Current.LastWeight.ShouldNotBeNull().Value.ShouldBe(1m);

        _clock.Advance(TimeSpan.FromSeconds(1));
        _store.ReportReading("c1", "s", Reading(1m));
        _store.ReportReading("c1", "s", Reading(1m, LiveWeightState.Motion));
        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.LastWeight.ShouldNotBeNull().State.ShouldBe(LiveWeightState.Motion);

        _store.DisconnectAgent("c1");
        (await watch.MoveNextAsync()).ShouldBeTrue();
        watch.Current.State.ShouldBe(DeviceConnectionState.Disconnected);
        watch.Current.LastWeight.ShouldNotBeNull().Value.ShouldBe(1m);
    }

    [Fact]
    public async Task Device_watch_ends_when_cancelled()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var watch = _store.WatchDeviceAsync("s", cancel.Token).GetAsyncEnumerator(cancel.Token);
        (await watch.MoveNextAsync()).ShouldBeTrue();
        var next = watch.MoveNextAsync();

        await cancel.CancelAsync();

        (await next).ShouldBeFalse();
    }

    [Fact]
    public async Task Slow_watcher_skips_old_readings_instead_of_blocking()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var watch = _store.WatchAsync("s", ct).GetAsyncEnumerator(ct);
        var first = watch.MoveNextAsync();

        for (var i = 0; i < 1000; i++)
        {
            _store.ReportReading("c1", "s", Reading(i));
        }

        (await first).ShouldBeTrue();
        var seen = new List<decimal?> { watch.Current.Value };
        while (seen[^1] != 999m && await watch.MoveNextAsync())
        {
            seen.Add(watch.Current.Value);
        }

        seen.Count.ShouldBeLessThan(1000);
        seen[^1].ShouldBe(999m);
    }

    [Fact]
    public async Task Concurrent_reports_are_safe()
    {
        var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                _store.ReportReading($"c{t}", $"s{t % 4}", Reading(i));
                _store.ReportStatus($"c{t}", $"s{t % 4}", Status(DeviceConnectionState.Connected));
                _ = _store.GetDevices();
            }

            _store.DisconnectAgent($"c{t}");
        }));

        await Task.WhenAll(tasks);

        _store.GetDevices().Count.ShouldBe(4);
    }
}
