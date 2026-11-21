using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Platform.Testing;
using Vyxum.Samples.Notifications;
using Vyxum.Samples.Orders;
using Xunit;

namespace Vyxum.IntegrationTests;

public class DataLayerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Module_tables_are_created_with_prefix_and_data_round_trips(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);
        await using var host = await TestModuleHost.StartAsync(database, new OrdersModule(), new NotificationsModule());

        Guid orderId;
        await using (var scope = host.CreateScope())
        {
            orderId = await scope.ServiceProvider.GetRequiredService<OrderService>().PlaceOrderAsync("Flour", 3, Ct);
        }

        await using (var scope = host.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Model.FindEntityType(typeof(Order))!.GetTableName().ShouldBe("orders_Orders");

            var order = await db.Orders.SingleAsync(Ct);
            order.Id.ShouldBe(orderId);
            order.Product.ShouldBe("Flour");
            order.Quantity.ShouldBe(3);
            order.PlacedAt.Kind.ShouldBe(DateTimeKind.Utc);
            order.PlacedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));

            var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            (await notifications.Notifications.CountAsync(Ct)).ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Restart_keeps_data_and_a_module_added_later_gets_its_tables(DatabaseProvider provider)
    {
        await using var database = Databases.CreateOrSkip(provider);

        await using (var first = await TestModuleHost.StartAsync(database, new OrdersModule()))
        {
            await using var scope = first.CreateScope();
            await scope.ServiceProvider.GetRequiredService<OrderService>().PlaceOrderAsync("Sugar", 1, Ct);
        }

        await using var second = await TestModuleHost.StartAsync(database, new OrdersModule(), new NotificationsModule());
        await second.Services.GetRequiredService<IModuleDatabaseInitializer>().InitializeAsync(Ct);

        await using (var scope = second.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.CountAsync(Ct)).ShouldBe(1);

            var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            notifications.Notifications.Add(new Notification { Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), Message = "hi", CreatedAt = DateTime.UtcNow });
            await notifications.SaveChangesAsync(Ct);
            (await notifications.Notifications.CountAsync(Ct)).ShouldBe(1);
        }
    }
}
