using Shouldly;
using Vyxum.Platform.Abstractions;
using Xunit;

namespace Vyxum.Platform.Tests.Modules;

public class ModuleValidatorTests
{
    private static IReadOnlyList<string> Problems(params TestModule[] modules) =>
        ModuleValidator.FindProblems(modules.Select(m => new ModuleDescriptor(m)).ToList());

    [Fact]
    public void Valid_modules_have_no_problems()
    {
        Problems(
            TestModule.Create("samples.orders", provides: ["samples.orders"], publishes: [typeof(OrderPlaced)]),
            TestModule.Create("samples.notifications", requires: ["samples.orders"], optional: ["samples.other"]))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Missing_required_capability_names_module_and_capability()
    {
        var problems = Problems(TestModule.Create("samples.notifications", requires: ["samples.orders"]));

        problems.ShouldHaveSingleItem()
            .ShouldBe("Module 'samples.notifications' requires capability 'samples.orders', but no module provides it.");
    }

    [Fact]
    public void Missing_optional_capability_is_not_a_problem_and_registry_reports_it_unavailable()
    {
        var module = TestModule.Create("samples.notifications", optional: ["samples.orders"]);

        Problems(module).ShouldBeEmpty();
        new CapabilityRegistry([module.Manifest]).IsAvailable("samples.orders").ShouldBeFalse();
    }

    [Fact]
    public void Registry_reports_provided_capability_available()
    {
        var module = TestModule.Create("samples.orders", provides: ["samples.orders"]);

        new CapabilityRegistry([module.Manifest]).IsAvailable("samples.orders").ShouldBeTrue();
    }

    [Fact]
    public void Capability_provided_by_two_modules_is_a_problem()
    {
        var problems = Problems(
            TestModule.Create("a", provides: ["shared.thing"]),
            TestModule.Create("b", provides: ["shared.thing"]));

        problems.ShouldHaveSingleItem().ShouldBe("Capability 'shared.thing' is provided by more than one module: 'a', 'b'.");
    }

    [Fact]
    public void Requires_cycle_is_reported_once()
    {
        var problems = Problems(
            TestModule.Create("a", provides: ["cap.a"], requires: ["cap.c"]),
            TestModule.Create("b", provides: ["cap.b"], requires: ["cap.a"]),
            TestModule.Create("c", provides: ["cap.c"], requires: ["cap.b"]));

        problems.ShouldHaveSingleItem().ShouldBe("Modules require each other's capabilities in a cycle: a -> c -> b -> a.");
    }

    [Fact]
    public void Requiring_own_capability_and_optional_back_references_are_not_cycles()
    {
        Problems(
            TestModule.Create("a", provides: ["cap.a"], requires: ["cap.a"], optional: ["cap.b"]),
            TestModule.Create("b", provides: ["cap.b"], requires: ["cap.a"]))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Duplicate_module_id_is_a_problem()
    {
        var problems = Problems(TestModule.Create("samples.orders"), TestModule.Create("samples.orders"));

        problems.ShouldHaveSingleItem().ShouldStartWith("Module id 'samples.orders' is used by more than one module");
    }

    [Theory]
    [InlineData("Samples.Orders")]
    [InlineData("samples_orders")]
    [InlineData("samples..orders")]
    [InlineData("")]
    public void Invalid_module_id_is_a_problem(string id)
    {
        Problems(TestModule.Create(id)).ShouldHaveSingleItem().ShouldContain($"invalid id '{id}'");
    }

    [Fact]
    public void Invalid_capability_id_is_a_problem()
    {
        Problems(TestModule.Create("a", provides: ["Bad Capability"]))
            .ShouldHaveSingleItem().ShouldBe("Module 'a' declares an invalid capability id 'Bad Capability'.");
    }

    [Fact]
    public void Event_name_and_version_published_by_two_modules_is_a_problem()
    {
        var problems = Problems(
            TestModule.Create("a", publishes: [typeof(OrderPlaced)]),
            TestModule.Create("b", publishes: [typeof(OrderPlacedCopy)]));

        problems.ShouldHaveSingleItem()
            .ShouldBe("Integration event 'samples.order-placed' v1 is published by more than one module: 'a', 'b'.");
    }

    [Fact]
    public void Event_name_and_version_on_two_types_of_one_module_is_a_problem()
    {
        Problems(TestModule.Create("a", publishes: [typeof(OrderPlaced), typeof(OrderPlacedCopy)]))
            .ShouldHaveSingleItem().ShouldStartWith("Module 'a' uses integration event 'samples.order-placed' v1 on more than one type");
    }

    [Fact]
    public void Same_event_name_with_different_versions_is_fine()
    {
        Problems(TestModule.Create("a", publishes: [typeof(OrderPlaced), typeof(OrderPlacedV2)])).ShouldBeEmpty();
    }

    [Fact]
    public void Event_without_attribute_is_a_problem()
    {
        Problems(TestModule.Create("a", publishes: [typeof(EventWithoutAttribute)]))
            .ShouldHaveSingleItem().ShouldContain("has no [IntegrationEvent] attribute");
    }

    [Fact]
    public void Event_with_invalid_name_or_version_is_a_problem()
    {
        Problems(TestModule.Create("a", publishes: [typeof(EventWithBadName)]))
            .ShouldHaveSingleItem().ShouldContain("invalid name or version");
    }

    [Fact]
    public void Published_type_that_is_not_an_event_is_a_problem()
    {
        Problems(TestModule.Create("a", publishes: [typeof(string)]))
            .ShouldHaveSingleItem().ShouldContain("does not implement IIntegrationEvent");
    }

    [Fact]
    public void All_problems_are_reported_together()
    {
        var exception = Should.Throw<ModuleValidationException>(() => ModuleValidator.Validate(
        [
            new ModuleDescriptor(TestModule.Create("a", provides: ["cap.x"], requires: ["cap.missing"])),
            new ModuleDescriptor(TestModule.Create("b", provides: ["cap.x"])),
        ]));

        exception.Problems.Count.ShouldBe(2);
        exception.Message.ShouldContain("Module validation failed with 2 problem(s)");
        exception.Message.ShouldContain("'cap.missing'");
        exception.Message.ShouldContain("'cap.x'");
    }

    [IntegrationEvent("samples.order-placed", 1)]
    private sealed record OrderPlaced(Guid OrderId) : IIntegrationEvent;

    [IntegrationEvent("samples.order-placed", 1)]
    private sealed record OrderPlacedCopy(Guid OrderId) : IIntegrationEvent;

    [IntegrationEvent("samples.order-placed", 2)]
    private sealed record OrderPlacedV2(Guid OrderId, string Product) : IIntegrationEvent;

    private sealed record EventWithoutAttribute : IIntegrationEvent;

    [IntegrationEvent("Bad Name", 0)]
    private sealed record EventWithBadName : IIntegrationEvent;

    [Fact]
    public void Ui_entry_must_be_an_embedded_resource_and_sdk_version_positive()
    {
        var module = new TestModule(new ModuleManifest
        {
            Id = "samples.screens",
            Version = new Version(1, 0),
            Ui = new ModuleUi { Entry = "index.js", SdkVersion = 0 },
        });

        Problems(module).ShouldBe(
        [
            "Module 'samples.screens' declares UI SDK version 0; use 1 or more.",
            "Module 'samples.screens' declares the UI entry 'index.js', but its assembly has no embedded resource 'vyxum-ui/index.js'.",
        ]);
    }

    [Fact]
    public void Preview_image_must_be_an_embedded_preview_resource()
    {
        var module = new TestModule(new ModuleManifest
        {
            Id = "samples.preview",
            Version = new Version(1, 0),
            Preview = new ModulePreview { DescriptionKey = "samples.Preview", Image = "screen.svg" },
        });

        Problems(module).ShouldBe(
        [
            "Module 'samples.preview' declares the preview image 'screen.svg', but its assembly has no embedded resource 'vyxum-preview/screen.svg'.",
        ]);
    }
}
