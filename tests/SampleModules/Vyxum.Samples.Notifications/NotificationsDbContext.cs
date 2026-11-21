using Microsoft.EntityFrameworkCore;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Notifications;

/// <summary>The Notifications module's data: table <c>notifications_Notifications</c>.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options)
{
    /// <inheritdoc />
    public override string TablePrefix => "notifications";

    /// <summary>Notifications.</summary>
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Message).HasMaxLength(500);
        });
    }
}

/// <summary>A notification about an order.</summary>
public sealed class Notification
{
    /// <summary>Notification id.</summary>
    public Guid Id { get; init; }

    /// <summary>The order this notification is about.</summary>
    public Guid OrderId { get; init; }

    /// <summary>Text.</summary>
    public required string Message { get; init; }

    /// <summary>UTC creation time.</summary>
    public DateTime CreatedAt { get; init; }
}
