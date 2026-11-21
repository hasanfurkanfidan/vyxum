using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Vyxum.Platform;
using Vyxum.Samples.Notifications;
using Vyxum.Samples.Orders;
using Xunit;

namespace Vyxum.IntegrationTests;

public class BackgroundDispatchTests
{
    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task Hosted_services_create_tables_and_deliver_events_in_the_background(DatabaseProvider provider)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = Databases.CreateOrSkip(provider);

        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(database.Settings);
        builder.Configuration[OutboxOptions.SectionName + ":PollingInterval"] = "00:00:00.050";
        builder.Services.AddVyxumPlatform(builder.Configuration, [typeof(OrdersModule).Assembly, typeof(NotificationsModule).Assembly]);
        using var host = builder.Build();
        await host.StartAsync(ct);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OrderService>().PlaceOrderAsync("Salt", 1, ct);
        }

        var deadline = DateTime.UtcNow.AddSeconds(15);
        var count = 0;
        while (count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, ct);
            await using var scope = host.Services.CreateAsyncScope();
            count = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Notifications.CountAsync(ct);
        }

        await host.StopAsync(ct);
        count.ShouldBe(1);
    }
}
