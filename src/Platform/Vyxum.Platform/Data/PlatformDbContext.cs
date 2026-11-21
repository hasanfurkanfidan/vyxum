using Microsoft.EntityFrameworkCore;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// Platform-owned tables. Migrations are generated per provider into <c>Migrations/Sqlite</c> and
/// <c>Migrations/SqlServer</c> and recorded in <see cref="MigrationsHistoryTable"/>.
/// </summary>
internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    /// <summary>The platform's migrations history table.</summary>
    public const string MigrationsHistoryTable = "__plt_migrations";

    public DbSet<InstalledModule> InstalledModules => Set<InstalledModule>();

    public DbSet<PlatformSetting> Settings => Set<PlatformSetting>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        ProviderMigrations.Configure(optionsBuilder, MigrationsHistoryTable);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstalledModule>(entity =>
        {
            entity.ToTable("__vyxum_modules");
            entity.HasKey(m => m.ModuleId);
            entity.Property(m => m.ModuleId).HasMaxLength(200);
            entity.Property(m => m.Version).HasMaxLength(50);
        });

        modelBuilder.Entity<PlatformSetting>(entity =>
        {
            entity.ToTable("plt_Settings");
            entity.HasKey(s => s.Key);
            entity.Property(s => s.Key).HasMaxLength(200);
            entity.Property(s => s.Value).HasMaxLength(4000);
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

/// <summary>One platform setting (<see cref="IPlatformSettings"/>).</summary>
internal sealed class PlatformSetting
{
    public required string Key { get; init; }

    public required string Value { get; set; }

    /// <summary>UTC time of the last change.</summary>
    public DateTime UpdatedAt { get; set; }
}
