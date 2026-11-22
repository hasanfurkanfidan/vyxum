using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Testing;
using Vyxum.Samples.Notifications;
using Vyxum.Samples.Orders;
using Vyxum.Samples.Paid;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>
/// Events for a paid module that may not run are held per handler, cost nothing while held, and are replayed once
/// the module may run again; on both providers.
/// </summary>
public sealed class LicenseOutboxTests : IDisposable
{
    private readonly TestLicenseIssuer _issuer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _issuer.Dispose();

    private static async Task ShouldUploadAsync(LicenseTestApp app, string file)
    {
        using var response = await app.UploadAsync(file);
        var (status, body) = await LicenseTestApp.ReadAsync(response);
        status.ShouldBe(200, body.ToString());
    }

    private static async Task<Guid> PlaceOrderAsync(LicenseTestApp app, string product = "Flour")
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrderService>().PlaceOrderAsync(product, 2, Ct);
        return await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().OutboxMessages
            .OrderByDescending(m => m.OccurredAt).Select(m => m.Id).FirstAsync(Ct);
    }

    private static async Task<T> QueryAsync<TContext, T>(LicenseTestApp app, Func<TContext, Task<T>> query)
        where TContext : DbContext
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    private static Task<OutboxMessage> MessageAsync(LicenseTestApp app, Guid id) =>
        QueryAsync<OrdersDbContext, OutboxMessage>(app, db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, Ct));

    private static Task<int> PaidEntriesAsync(LicenseTestApp app) =>
        QueryAsync<PaidDbContext, int>(app, db => db.Entries.CountAsync(Ct));

    private static Task<int> NotificationsAsync(LicenseTestApp app) =>
        QueryAsync<NotificationsDbContext, int>(app, db => db.Notifications.CountAsync(Ct));

    /// <summary>Delivers until nothing is due; returns the number of messages attempted.</summary>
    private static async Task<int> DispatchAllAsync(LicenseTestApp app)
    {
        var total = 0;
        int attempted;
        while ((attempted = await app.DispatchAsync()) > 0)
        {
            total += attempted;
        }

        return total;
    }

    /// <summary>Adds <paramref name="count"/> messages as the dispatcher leaves them when only the paid handler waits.</summary>
    private static async Task AddHeldMessagesAsync(LicenseTestApp app, int count)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var occurredAt = DateTime.UtcNow.AddDays(-1);
        for (var i = 0; i < count; i++)
        {
            var message = OutboxMessage.Create(new Vyxum.Samples.Orders.OrderPlaced(Guid.NewGuid(), $"Held {i}", 1), occurredAt.AddMilliseconds(i));
            message.LastError = OutboxDispatcher.HoldError([PaidModule.Id]);
            message.NextAttemptAt = OutboxDispatcher.HeldUntil;
            orders.OutboxMessages.Add(message);
            notifications.InboxMessages.Add(new InboxMessage
            {
                MessageId = message.Id,
                HandlerName = typeof(Vyxum.Samples.Notifications.OrderPlacedHandler).FullName!,
                ProcessedAt = occurredAt,
            });
        }

        await orders.SaveChangesAsync(Ct);
        await notifications.SaveChangesAsync(Ct);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Thousands_of_held_messages_do_not_delay_a_new_message(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var app = new LicenseTestApp(database, _issuer, maintenance: false);
        await app.DispatchAsync();
        await AddHeldMessagesAsync(app, 3000);

        // Held messages are older, but they are not polled: the new message is the only one attempted.
        var id = await PlaceOrderAsync(app, "New");
        (await app.DispatchAsync()).ShouldBe(1);

        (await NotificationsAsync(app)).ShouldBe(1);
        var message = await MessageAsync(app, id);
        message.NextAttemptAt.ShouldBe(OutboxDispatcher.HeldUntil);
        message.Attempts.ShouldBe(0);
        (await app.DispatchAsync()).ShouldBe(0);

        // A license change that does not let the paid module run releases nothing.
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(modules: ["samples.other"])));
        (await app.DispatchAsync()).ShouldBe(0);
        (await PaidEntriesAsync(app)).ShouldBe(0);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Held_messages_are_replayed_once_after_read_only_is_renewed(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        var clock = new TestClock();
        await using var app = new LicenseTestApp(database, _issuer, clock, maintenance: false);
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(validUntil: TestLicenseIssuer.Today, graceDays: 1)));

        // Licensed: handled at once.
        await PlaceOrderAsync(app, "Licensed");
        (await DispatchAllAsync(app)).ShouldBe(1);
        (await PaidEntriesAsync(app)).ShouldBe(1);

        // Read-only: the paid handler is held, the free one runs.
        clock.Advance(TimeSpan.FromDays(2));
        app.Services.GetRequiredService<ILicenseState>().Status.ShouldBe(LicenseCondition.ReadOnly);
        var held = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            held.Add(await PlaceOrderAsync(app, $"Read-only {i}"));
        }

        await AddHeldMessagesAsync(app, 200);
        (await DispatchAllAsync(app)).ShouldBe(5);
        (await NotificationsAsync(app)).ShouldBe(6);
        (await PaidEntriesAsync(app)).ShouldBe(1);
        foreach (var id in held)
        {
            var message = await MessageAsync(app, id);
            message.ProcessedAt.ShouldBeNull();
            message.NextAttemptAt.ShouldBe(OutboxDispatcher.HeldUntil);
            message.LastError.ShouldBe(OutboxDispatcher.HoldError([PaidModule.Id]));
        }

        (await app.DispatchAsync()).ShouldBe(0);

        // Renewed: every held message is delivered to the paid module once; the free handler does not run again.
        await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload(licenseId: "lic-2")));
        (await DispatchAllAsync(app)).ShouldBe(205);
        (await PaidEntriesAsync(app)).ShouldBe(206);
        (await NotificationsAsync(app)).ShouldBe(6);
        foreach (var id in held)
        {
            var message = await MessageAsync(app, id);
            message.ProcessedAt.ShouldNotBeNull();
            message.LastError.ShouldBeNull();
            message.Attempts.ShouldBe(1);
        }

        (await QueryAsync<PaidDbContext, int>(app, db => db.InboxMessages.CountAsync(Ct))).ShouldBe(206);
        (await app.DispatchAsync()).ShouldBe(0);
        (await QueryAsync<OrdersDbContext, int>(app, db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null, Ct))).ShouldBe(0);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Messages_held_before_a_restart_are_released_when_the_module_may_run(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using (var app = new LicenseTestApp(database, _issuer, maintenance: false))
        {
            await PlaceOrderAsync(app);
            await DispatchAllAsync(app);
            await ShouldUploadAsync(app, _issuer.Sign(TestLicenseIssuer.Payload()));
        }

        await using (var app = new LicenseTestApp(database, _issuer, maintenance: false))
        {
            await DispatchAllAsync(app);
            (await PaidEntriesAsync(app)).ShouldBe(1);
            (await NotificationsAsync(app)).ShouldBe(1);
        }
    }

    [Fact]
    public void The_hold_marker_names_each_module_exactly()
    {
        var error = OutboxDispatcher.HoldError(["b.paid", "a.paid", "b.paid"]);

        error.ShouldBe(OutboxDispatcher.LicenseWaitPrefix + "|a.paid|b.paid|");
        error.ShouldNotContain("|paid|");
    }
}
