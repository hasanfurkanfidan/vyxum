using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Tests.Modules;
using Xunit;

namespace Vyxum.Platform.Tests.Events;

public class IntegrationEventTests
{
    [Fact]
    public void Outbox_message_carries_name_version_and_camel_case_json()
    {
        var occurredAt = new DateTime(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);

        var message = OutboxMessage.Create(new ThingHappened(42, "x"), occurredAt);

        message.EventName.ShouldBe("tests.thing-happened");
        message.EventVersion.ShouldBe(1);
        message.Payload.ShouldBe("""{"thingId":42,"note":"x"}""");
        message.OccurredAt.ShouldBe(occurredAt);
        message.ProcessedAt.ShouldBeNull();
        message.Attempts.ShouldBe(0);
        IntegrationEventSerializer.Deserialize(message.Payload, typeof(ThingHappened)).ShouldBe(new ThingHappened(42, "x"));
    }

    [Fact]
    public void Publishing_an_event_without_attribute_fails()
    {
        Should.Throw<InvalidOperationException>(() => OutboxMessage.Create(new Unnamed(), DateTime.UtcNow))
            .Message.ShouldContain("has no [IntegrationEvent] attribute");
    }

    [Fact]
    public void Handler_module_without_DbContext_is_a_problem()
    {
        var module = new TestModule(
            new ModuleManifest { Id = "a", Version = new Version(1, 0) },
            (_, c) => c.AddEventHandler<ThingHappened, ThingHandler>());

        Should.Throw<ModuleValidationException>(() => Register(module))
            .Problems.ShouldHaveSingleItem().ShouldContain("registers integration event handlers but no DbContext");
    }

    [Fact]
    public void Handled_event_without_attribute_is_a_problem()
    {
        var module = new TestModule(
            new ModuleManifest { Id = "a", Version = new Version(1, 0) },
            (_, c) =>
            {
                c.AddDbContext<EmptyDbContext>();
                c.AddEventHandler<Unnamed, UnnamedHandler>();
            });

        Should.Throw<ModuleValidationException>(() => Register(module))
            .Problems.ShouldHaveSingleItem().ShouldContain("has no [IntegrationEvent] attribute");
    }

    [Fact]
    public void Module_handling_two_types_with_one_event_name_is_a_problem()
    {
        var module = new TestModule(
            new ModuleManifest { Id = "a", Version = new Version(1, 0), Publishes = [typeof(ThingHappened)] },
            (_, c) =>
            {
                c.AddDbContext<EmptyDbContext>();
                c.AddEventHandler<ThingHappenedCopy, ThingCopyHandler>();
            });

        Should.Throw<ModuleValidationException>(() => Register(module))
            .Problems.ShouldHaveSingleItem().ShouldStartWith("Module 'a' uses integration event 'tests.thing-happened' v1 on more than one type");
    }

    [Fact]
    public void Different_modules_may_each_keep_a_copy_of_an_event_they_handle()
    {
        var publisher = TestModule.Create("publisher", publishes: [typeof(ThingHappened)]);
        var consumer = new TestModule(
            new ModuleManifest { Id = "consumer", Version = new Version(1, 0) },
            (_, c) =>
            {
                c.AddDbContext<EmptyDbContext>();
                c.AddEventHandler<ThingHappenedCopy, ThingCopyHandler>();
            });

        var catalog = Register(publisher, consumer);

        catalog.Modules.Single(m => m.Manifest.Id == "consumer").EventHandlers.ShouldHaveSingleItem()
            .HandlerName.ShouldBe(typeof(ThingCopyHandler).FullName);
    }

    private static ModuleCatalog Register(params TestModule[] modules) =>
        new ServiceCollection().AddVyxumPlatform(new ConfigurationBuilder().Build(), modules);

    [IntegrationEvent("tests.thing-happened", 1)]
    private sealed record ThingHappened(int ThingId, string Note) : IIntegrationEvent;

    [IntegrationEvent("tests.thing-happened", 1)]
    private sealed record ThingHappenedCopy(int ThingId) : IIntegrationEvent;

    private sealed record Unnamed : IIntegrationEvent;

    private sealed class ThingHandler : IIntegrationEventHandler<ThingHappened>
    {
        public Task HandleAsync(ThingHappened integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ThingCopyHandler : IIntegrationEventHandler<ThingHappenedCopy>
    {
        public Task HandleAsync(ThingHappenedCopy integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class UnnamedHandler : IIntegrationEventHandler<Unnamed>
    {
        public Task HandleAsync(Unnamed integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class EmptyDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<EmptyDbContext> options) : ModuleDbContext(options)
    {
        public override string TablePrefix => "empty";

        protected override void ConfigureModel(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
        }
    }
}
