using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Testing;
using Vyxum.Samples.Notifications;
using Vyxum.Samples.Orders;
using Xunit;

namespace Vyxum.IntegrationTests;

/// <summary>An event flows from the Orders module to the Notifications module through outbox and inbox.</summary>
public class EventFlowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<TestModuleHost> StartAsync(TestDatabase database) =>
        TestModuleHost.StartAsync(database, new OrdersModule(), new NotificationsModule());

    private static async Task<Guid> PlaceOrderAsync(TestModuleHost host)
    {
        await using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OrderService>().PlaceOrderAsync("Flour", 2, Ct);
    }

    private static async Task<T> QueryAsync<TContext, T>(TestModuleHost host, Func<TContext, Task<T>> query)
        where TContext : DbContext
    {
        await using var scope = host.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    private static Task<List<Notification>> NotificationsAsync(TestModuleHost host) =>
        QueryAsync<NotificationsDbContext, List<Notification>>(host, db => db.Notifications.ToListAsync(Ct));

    private static Task<Platform.Abstractions.OutboxMessage> SingleOutboxMessageAsync(TestModuleHost host) =>
        QueryAsync<OrdersDbContext, Platform.Abstractions.OutboxMessage>(host, db => db.OutboxMessages.SingleAsync(Ct));

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Placed_order_creates_exactly_one_notification(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var host = await StartAsync(database);

        var orderId = await PlaceOrderAsync(host);

        var pending = await SingleOutboxMessageAsync(host);
        pending.EventName.ShouldBe("samples.order-placed");
        pending.EventVersion.ShouldBe(1);
        pending.ProcessedAt.ShouldBeNull();

        (await host.DispatchPendingAsync(Ct)).ShouldBe(1);
        (await host.DispatchPendingAsync(Ct)).ShouldBe(0);

        var notification = (await NotificationsAsync(host)).ShouldHaveSingleItem();
        notification.OrderId.ShouldBe(orderId);
        notification.Message.ShouldBe("Order placed: 2 x Flour");

        var processed = await SingleOutboxMessageAsync(host);
        processed.ProcessedAt.ShouldNotBeNull();
        processed.Attempts.ShouldBe(1);
        processed.LastError.ShouldBeNull();

        var inbox = await QueryAsync<NotificationsDbContext, List<Platform.Abstractions.InboxMessage>>(host, db => db.InboxMessages.ToListAsync(Ct));
        inbox.ShouldHaveSingleItem().MessageId.ShouldBe(processed.Id);
        inbox[0].HandlerName.ShouldBe(typeof(OrderPlacedHandler).FullName);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Rolled_back_business_transaction_leaves_no_event(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var host = await StartAsync(database);

        await using (var scope = host.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await Should.ThrowAsync<InvalidOperationException>(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(Ct);
                await scope.ServiceProvider.GetRequiredService<OrderService>().PlaceOrderAsync("Flour", 2, Ct);
                throw new InvalidOperationException("Simulated failure before commit.");
            });
        }

        (await QueryAsync<OrdersDbContext, int>(host, db => db.Orders.CountAsync(Ct))).ShouldBe(0);
        (await QueryAsync<OrdersDbContext, int>(host, db => db.OutboxMessages.CountAsync(Ct))).ShouldBe(0);
        (await host.DispatchPendingAsync(Ct)).ShouldBe(0);
        (await NotificationsAsync(host)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Failing_handler_is_retried_later_and_its_effect_happens_once(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var host = await StartAsync(database);
        host.Services.GetRequiredService<HandlerFaults>().Remaining = 1;

        await PlaceOrderAsync(host);
        await host.DispatchPendingAsync(Ct);

        (await NotificationsAsync(host)).ShouldBeEmpty();
        var failed = await SingleOutboxMessageAsync(host);
        failed.ProcessedAt.ShouldBeNull();
        failed.Attempts.ShouldBe(1);
        failed.LastError.ShouldNotBeNull().ShouldContain("Simulated handler failure.");
        failed.LastError.ShouldContain(typeof(OrderPlacedHandler).FullName!);
        failed.NextAttemptAt.ShouldNotBeNull().ShouldBeGreaterThan(host.Clock.GetUtcNow().UtcDateTime);

        // Not due yet: the backoff delay has not passed.
        (await host.DispatchPendingAsync(Ct)).ShouldBe(0);

        host.Clock.Advance(new OutboxOptions().RetryBaseDelay + TimeSpan.FromSeconds(1));
        (await host.DispatchPendingAsync(Ct)).ShouldBe(1);

        (await NotificationsAsync(host)).ShouldHaveSingleItem();
        var processed = await SingleOutboxMessageAsync(host);
        processed.ProcessedAt.ShouldNotBeNull();
        processed.Attempts.ShouldBe(2);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Message_delivered_again_after_crash_is_handled_once(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var host = await StartAsync(database);

        await PlaceOrderAsync(host);
        await host.DispatchPendingAsync(Ct);

        // Simulate a crash after the handler committed but before the message was marked processed.
        await using (var scope = host.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var message = await db.OutboxMessages.SingleAsync(Ct);
            message.ProcessedAt = null;
            await db.SaveChangesAsync(Ct);
        }

        (await host.DispatchPendingAsync(Ct)).ShouldBe(1);

        (await NotificationsAsync(host)).ShouldHaveSingleItem();
        (await QueryAsync<NotificationsDbContext, int>(host, db => db.InboxMessages.CountAsync(Ct))).ShouldBe(1);
        var processed = await SingleOutboxMessageAsync(host);
        processed.ProcessedAt.ShouldNotBeNull();
        processed.Attempts.ShouldBe(2);
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Message_without_handlers_is_marked_processed(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var host = await TestModuleHost.StartAsync(database, new OrdersModule());

        await PlaceOrderAsync(host);
        (await host.DispatchPendingAsync(Ct)).ShouldBe(1);

        (await SingleOutboxMessageAsync(host)).ProcessedAt.ShouldNotBeNull();
    }
}
