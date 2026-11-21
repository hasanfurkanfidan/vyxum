using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Orders;

/// <summary>Sample module A: stores orders. Test-only, never shipped.</summary>
public sealed class OrdersModule : IModule
{
    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "samples.orders",
        Version = new Version(1, 0),
        Provides = ["samples.orders"],
        Publishes = [typeof(OrderPlaced)],
    };

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        context.AddDbContext<OrdersDbContext>();
        services.AddScoped<OrderService>();
    }
}
