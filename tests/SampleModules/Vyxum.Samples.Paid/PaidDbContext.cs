using Microsoft.EntityFrameworkCore;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Paid;

/// <summary>The paid sample module's data: table <c>paid_Entries</c>.</summary>
public sealed class PaidDbContext(DbContextOptions<PaidDbContext> options) : ModuleDbContext(options)
{
    /// <inheritdoc />
    public override string TablePrefix => "paid";

    /// <summary>Entries.</summary>
    public DbSet<Entry> Entries => Set<Entry>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entry>(entity =>
        {
            entity.ToTable("Entries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Text).HasMaxLength(500);
        });
    }
}

/// <summary>An entry.</summary>
public sealed class Entry
{
    /// <summary>Entry id.</summary>
    public Guid Id { get; init; }

    /// <summary>Text.</summary>
    public required string Text { get; init; }

    /// <summary>UTC creation time.</summary>
    public DateTime CreatedAt { get; init; }
}
