using Microsoft.EntityFrameworkCore;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Weighing;

/// <summary>The Weighing module's data: tables <c>wgh_Products</c> (local product copy) and <c>wgh_Records</c>.</summary>
public sealed class WeighingDbContext(DbContextOptions<WeighingDbContext> options) : ModuleDbContext(options)
{
    /// <inheritdoc />
    public override string TablePrefix => "wgh";

    /// <summary>Local copy of the product catalog, kept up to date from <see cref="ProductSaved"/>.</summary>
    public DbSet<WeighingProduct> Products => Set<WeighingProduct>();

    /// <summary>Weighing records.</summary>
    public DbSet<WeighingRecord> Records => Set<WeighingRecord>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WeighingProduct>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(p => p.ProductId);
            entity.Property(p => p.ProductId).ValueGeneratedNever();
            entity.Property(p => p.Code).IsRequired().HasMaxLength(WeighingLimits.ProductCodeMaxLength);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(WeighingLimits.ProductNameMaxLength);
            entity.Property(p => p.UnitPrice).HasPrecision(18, 4);
            entity.Property(p => p.PackageTare).HasPrecision(18, 4);
            entity.Property(p => p.TargetWeight).HasPrecision(18, 4);
            entity.Property(p => p.LowerTolerance).HasPrecision(18, 4);
            entity.Property(p => p.UpperTolerance).HasPrecision(18, 4);
            entity.Property(p => p.BarcodePrefix).HasMaxLength(WeighingLimits.BarcodePrefixMaxLength);
            entity.HasIndex(p => p.Code);
        });

        modelBuilder.Entity<WeighingRecord>(entity =>
        {
            entity.ToTable("Records");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedOnAdd();
            entity.Property(r => r.DeviceId).IsRequired().HasMaxLength(WeighingLimits.DeviceIdMaxLength);
            entity.Property(r => r.ProductCode).HasMaxLength(WeighingLimits.ProductCodeMaxLength);
            entity.Property(r => r.ProductName).HasMaxLength(WeighingLimits.ProductNameMaxLength);
            entity.Property(r => r.Lot).HasMaxLength(WeighingLimits.LotMaxLength);
            entity.Property(r => r.Gross).HasPrecision(18, 6);
            entity.Property(r => r.Tare).HasPrecision(18, 6);
            entity.Property(r => r.Net).HasPrecision(18, 6);
            entity.Property(r => r.Unit).HasMaxLength(WeighingLimits.UnitMaxLength);
            entity.Property(r => r.UnitPrice).HasPrecision(18, 4);
            entity.Property(r => r.TargetWeight).HasPrecision(18, 4);
            entity.Property(r => r.LowerTolerance).HasPrecision(18, 4);
            entity.Property(r => r.UpperTolerance).HasPrecision(18, 4);
            entity.Property(r => r.Amount).HasPrecision(18, WeighingLimits.AmountMaxDecimals);
            entity.Property(r => r.Currency).IsRequired().HasMaxLength(WeighingLimits.CurrencyMaxLength);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(16).IsConcurrencyToken();
            entity.Property(r => r.CancelReason).HasMaxLength(WeighingLimits.CancelReasonMaxLength);
            entity.Property(r => r.Operator).HasMaxLength(WeighingLimits.OperatorMaxLength);
            entity.HasIndex(r => r.RequestId).IsUnique();
            entity.HasIndex(r => r.CreatedAtUtc);
            entity.HasIndex(r => new { r.DeviceId, r.Id });
            entity.HasIndex(r => r.ProductId);
            entity.HasIndex(r => r.Lot);
        });
    }
}

/// <summary>Maximum lengths and scales of the Weighing module's data.</summary>
public static class WeighingLimits
{
    /// <summary>Maximum length of a product code (same as the Products module).</summary>
    public const int ProductCodeMaxLength = 32;

    /// <summary>Maximum length of a product name (same as the Products module).</summary>
    public const int ProductNameMaxLength = 200;

    /// <summary>Maximum length of a barcode prefix (same as the Products module).</summary>
    public const int BarcodePrefixMaxLength = 16;

    /// <summary>Maximum length of a device id.</summary>
    public const int DeviceIdMaxLength = 100;

    /// <summary>Maximum length of a lot.</summary>
    public const int LotMaxLength = 64;

    /// <summary>Maximum length of a unit.</summary>
    public const int UnitMaxLength = 16;

    /// <summary>Maximum length of a currency code.</summary>
    public const int CurrencyMaxLength = 8;

    /// <summary>Maximum length of a cancel reason.</summary>
    public const int CancelReasonMaxLength = 500;

    /// <summary>Maximum length of an operator name.</summary>
    public const int OperatorMaxLength = 100;

    /// <summary>Maximum decimals of a stored weight.</summary>
    public const int WeightMaxDecimals = 6;

    /// <summary>Maximum decimals of an amount.</summary>
    public const int AmountMaxDecimals = 4;
}

/// <summary>A product as the Weighing module knows it, copied from <see cref="ProductSaved"/>.</summary>
public sealed class WeighingProduct
{
    /// <summary>Product id (the Products module's id).</summary>
    public Guid ProductId { get; init; }

    /// <summary>PLU code.</summary>
    public required string Code { get; set; }

    /// <summary>Product name.</summary>
    public required string Name { get; set; }

    /// <summary>Price per kg.</summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>Package tare in kg.</summary>
    public decimal? PackageTare { get; set; }

    /// <summary>Target net weight in kg.</summary>
    public decimal? TargetWeight { get; set; }

    /// <summary>Allowed amount below <see cref="TargetWeight"/> in kg.</summary>
    public decimal? LowerTolerance { get; set; }

    /// <summary>Allowed amount above <see cref="TargetWeight"/> in kg.</summary>
    public decimal? UpperTolerance { get; set; }

    /// <summary>Shelf life in days.</summary>
    public int? ShelfLifeDays { get; set; }

    /// <summary>Barcode prefix.</summary>
    public string? BarcodePrefix { get; set; }

    /// <summary>Whether the product can be weighed.</summary>
    public bool IsActive { get; set; }

    /// <summary>UTC publish time of the event this copy was last updated from; older events are ignored.</summary>
    public DateTime SourceOccurredAtUtc { get; set; }
}

/// <summary>Status of a weighing record.</summary>
public enum WeighingStatus
{
    /// <summary>Saved and counted in totals.</summary>
    Saved,

    /// <summary>Cancelled; kept, but not counted in totals.</summary>
    Cancelled,
}

/// <summary>A weighing record. Weights are in <see cref="Unit"/>, prices per kg.</summary>
public sealed class WeighingRecord
{
    /// <summary>Record id; also the record number.</summary>
    public long Id { get; init; }

    /// <summary>The client's request id; unique, so a repeated request does not create a second record.</summary>
    public Guid RequestId { get; init; }

    /// <summary>UTC time the record was saved.</summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>The scale the weight was read from.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Product, if any.</summary>
    public Guid? ProductId { get; init; }

    /// <summary>Product code at the time of weighing.</summary>
    public string? ProductCode { get; init; }

    /// <summary>Product name at the time of weighing.</summary>
    public string? ProductName { get; init; }

    /// <summary>Lot, if any.</summary>
    public string? Lot { get; init; }

    /// <summary>Gross weight.</summary>
    public decimal Gross { get; init; }

    /// <summary>Decimals of <see cref="Gross"/> as read from the scale; kept because SQL Server pads decimals.</summary>
    public int GrossScale { get; init; }

    /// <summary>Tare.</summary>
    public decimal Tare { get; init; }

    /// <summary>Decimals of <see cref="Tare"/>.</summary>
    public int TareScale { get; init; }

    /// <summary>Net weight; shown with max(<see cref="GrossScale"/>, <see cref="TareScale"/>) decimals.</summary>
    public decimal Net { get; init; }

    /// <summary>
    /// <see cref="Net"/> times 10^(net decimals): an exact integer, so net totals per unit can be summed
    /// in SQL on every provider (SQLite cannot sum decimals).
    /// </summary>
    public long NetUnits { get; init; }

    /// <summary>Net weight in milligrams, rounded; for ordering and cross-unit sums. Null when the unit is unknown.</summary>
    public long? NetMilligrams { get; init; }

    /// <summary>Weight unit: normalized ("kg", "g", ...) when known, otherwise as reported; null when the scale sent none.</summary>
    public string? Unit { get; init; }

    /// <summary>Price per kg at the time of weighing.</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>Target net weight in kg on the product when the record was saved; null without a product or target.</summary>
    public decimal? TargetWeight { get; init; }

    /// <summary>Allowed amount below the target in kg when the record was saved.</summary>
    public decimal? LowerTolerance { get; init; }

    /// <summary>Allowed amount above the target in kg when the record was saved.</summary>
    public decimal? UpperTolerance { get; init; }

    /// <summary>Amount, rounded to <see cref="AmountScale"/> decimals; null without a price.</summary>
    public decimal? Amount { get; init; }

    /// <summary><see cref="Amount"/> in minor units (times 10^<see cref="AmountScale"/>), for sums in SQL.</summary>
    public long? AmountMinor { get; init; }

    /// <summary>Decimals the amount was rounded to.</summary>
    public int AmountScale { get; init; }

    /// <summary>Currency of the amount.</summary>
    public required string Currency { get; init; }

    /// <summary>Saved or cancelled.</summary>
    public WeighingStatus Status { get; set; }

    /// <summary>UTC time of the cancel.</summary>
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>Cancel reason.</summary>
    public string? CancelReason { get; set; }

    /// <summary>Operator; empty until a users module exists.</summary>
    public string? Operator { get; init; }
}
