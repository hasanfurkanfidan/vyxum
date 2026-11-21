using Microsoft.EntityFrameworkCore;

namespace Vyxum.Platform;

/// <summary>Platform-owned tables. In F0 only the record of which module tables have been created.</summary>
internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<InstalledModule> InstalledModules => Set<InstalledModule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstalledModule>(entity =>
        {
            entity.ToTable("__vyxum_modules");
            entity.HasKey(m => m.ModuleId);
            entity.Property(m => m.ModuleId).HasMaxLength(200);
            entity.Property(m => m.Version).HasMaxLength(50);
        });
    }
}

/// <summary>A module whose tables exist in the database.</summary>
internal sealed class InstalledModule
{
    public required string ModuleId { get; init; }

    public required string Version { get; set; }

    /// <summary>UTC time the module's tables were created.</summary>
    public DateTime InstalledAt { get; init; }
}
