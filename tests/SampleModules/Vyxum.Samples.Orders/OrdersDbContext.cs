using Microsoft.EntityFrameworkCore;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Orders;

/// <summary>The Orders module's data: table <c>orders_Orders</c>.</summary>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : ModuleDbContext(options)
{
    /// <inheritdoc />
    public override string TablePrefix => "orders";

    /// <summary>Orders.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Product).HasMaxLength(200);
        });
    }
}

/// <summary>An order.</summary>
public sealed class Order
{
    /// <summary>Order id.</summary>
    public Guid Id { get; init; }

    /// <summary>Ordered product.</summary>
    public required string Product { get; init; }

    /// <summary>Ordered quantity.</summary>
    public int Quantity { get; init; }

    /// <summary>UTC time the order was placed.</summary>
    public DateTime PlacedAt { get; init; }
}
