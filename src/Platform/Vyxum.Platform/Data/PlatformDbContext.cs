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

        modelBuilder.Entity<DeviceConfig>(entity =>
        {
            entity.ToTable("plt_Devices");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).HasMaxLength(DeviceConfigLimits.IdMaxLength);
            entity.Property(d => d.Name).IsRequired().HasMaxLength(DeviceConfigLimits.NameMaxLength);
            entity.Property(d => d.AgentName).HasMaxLength(DeviceConfigLimits.AgentNameMaxLength);
            entity.Property(d => d.TransportJson).IsRequired().HasMaxLength(DeviceConfigLimits.SettingsMaxLength);
            entity.Property(d => d.ProtocolJson).IsRequired().HasMaxLength(DeviceConfigLimits.SettingsMaxLength);
            entity.Property(d => d.Capacity).HasPrecision(18, DeviceConfigLimits.MaxDecimals);
            entity.Property(d => d.Division).HasPrecision(18, DeviceConfigLimits.MaxDecimals);
            entity.Property(d => d.Unit).HasMaxLength(DeviceConfigLimits.UnitMaxLength);
        });
    }
}

/// <summary>The settings of one device (decision V-39), table <c>plt_Devices</c>.</summary>
internal sealed class DeviceConfig
{
    /// <summary>The device id, for example <c>scale-1</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; set; }

    /// <summary>The agent that serves the device; null means the default agent (see <see cref="AgentDeviceAssignments"/>).</summary>
    public string? AgentName { get; set; }

    /// <summary>Transport settings as JSON, opaque to the platform except for <c>type</c>.</summary>
    public required string TransportJson { get; set; }

    /// <summary>Protocol settings as JSON, opaque to the platform except for <c>type</c>.</summary>
    public required string ProtocolJson { get; set; }

    public decimal? Capacity { get; set; }

    public decimal? Division { get; set; }

    public string? Unit { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>UTC creation time.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>UTC time of the last change.</summary>
    public DateTime UpdatedAt { get; set; }
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
