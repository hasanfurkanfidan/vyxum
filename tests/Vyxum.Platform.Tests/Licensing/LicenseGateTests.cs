using Shouldly;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Platform.Tests.Licensing;

public class LicenseGateTests
{
    [Theory]
    [InlineData(ModuleLicenseState.Free, "POST", null)]
    [InlineData(ModuleLicenseState.Licensed, "DELETE", null)]
    [InlineData(ModuleLicenseState.Grace, "PUT", null)]
    [InlineData(ModuleLicenseState.ReadOnly, "GET", null)]
    [InlineData(ModuleLicenseState.ReadOnly, "HEAD", null)]
    [InlineData(ModuleLicenseState.ReadOnly, "POST", "license_read_only")]
    [InlineData(ModuleLicenseState.ReadOnly, "PATCH", "license_read_only")]
    [InlineData(ModuleLicenseState.ReadOnly, "OPTIONS", "license_read_only")]
    [InlineData(ModuleLicenseState.Locked, "GET", "license_required")]
    [InlineData(ModuleLicenseState.Locked, "POST", "license_required")]
    public void Deny_follows_the_module_state_and_the_method(ModuleLicenseState state, string method, string? expected) =>
        LicenseGate.Deny(state, method).ShouldBe(expected);
}

public class LicenseStationsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    public void Unreadable_choice_is_empty(string? json) => LicenseStations.Parse(json).ShouldBeEmpty();

    [Fact]
    public void Parse_keeps_order_and_drops_duplicates_and_nulls() =>
        LicenseStations.Parse("""["b", null, "a", "b"]""").ShouldBe(["b", "a"]);

    [Fact]
    public void Effective_keeps_existing_devices_in_order_up_to_the_limit()
    {
        var existing = new HashSet<string> { "a", "b", "c" };

        LicenseStations.Effective(["c", "gone", "a", "b"], existing, 2).ShouldBe(["c", "a"]);
        LicenseStations.Effective(["c"], existing, 0).ShouldBeEmpty();
    }

    [Fact]
    public void Serialize_round_trips() =>
        LicenseStations.Parse(LicenseStations.Serialize(["scale-1", "scale-2"])).ShouldBe(["scale-1", "scale-2"]);
}
