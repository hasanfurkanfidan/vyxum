using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Base class for the single DbContext every module owns. All modules share one database without
/// schemas (SQLite has none), so every table of the module is named <c>{TablePrefix}_{Table}</c>
/// automatically. <see cref="DateTime"/> values are stored and read as UTC.
/// The context also holds the module's outbox and inbox tables; see <see cref="Publish"/>.
/// Register it with <see cref="ModuleContext.AddDbContext{TContext}"/>.
/// </summary>
public abstract partial class ModuleDbContext : DbContext
{
    /// <summary>Creates the context.</summary>
    protected ModuleDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>
    /// Prefix for every table of this module, e.g. <c>orders</c> gives <c>orders_Orders</c>.
    /// Letters and digits only, starting with a letter; unique across modules.
    /// </summary>
    public abstract string TablePrefix { get; }

    /// <summary>Integration events this module published, waiting for or done with delivery.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Integration events this module's handlers have processed.</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>
    /// Adds <paramref name="integrationEvent"/> to the outbox. It is written by the next
    /// <c>SaveChanges</c>, in the same transaction as the module's other changes, and delivered afterwards.
    /// </summary>
    public void Publish(IIntegrationEvent integrationEvent) =>
        OutboxMessages.Add(OutboxMessage.Create(integrationEvent, DateTime.UtcNow));

    /// <summary>Configures the module's entities. Table names are prefixed afterwards.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    /// <inheritdoc />
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (!PrefixPattern().IsMatch(TablePrefix ?? string.Empty))
        {
            throw new InvalidOperationException(
                $"{GetType().Name}.TablePrefix '{TablePrefix}' is invalid. Use letters and digits only, starting with a letter.");
        }

        ConfigureModel(modelBuilder);
        ConfigureMessaging(modelBuilder);
        ApplyTablePrefix(modelBuilder.Model, TablePrefix!);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private static void ConfigureMessaging(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).ValueGeneratedNever();
            entity.Property(m => m.EventName).HasMaxLength(200);
            entity.Property(m => m.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);
            entity.HasIndex(m => new { m.ProcessedAt, m.NextAttemptAt });
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("InboxMessages");
            entity.HasKey(m => new { m.MessageId, m.HandlerName });
            entity.Property(m => m.HandlerName).HasMaxLength(300);
        });
    }

    private static void ApplyTablePrefix(IMutableModel model, string prefix)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            // Owned types without their own table follow their owner's (already renamed) table.
            if (entityType.IsOwned() && entityType.FindAnnotation(RelationalAnnotationNames.TableName) is null)
            {
                continue;
            }

            var tableName = entityType.GetTableName();
            if (tableName is not null && !tableName.StartsWith(prefix + "_", StringComparison.Ordinal))
            {
                entityType.SetTableName($"{prefix}_{tableName}");
            }
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex PrefixPattern();

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
