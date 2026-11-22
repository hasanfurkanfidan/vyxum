using Microsoft.EntityFrameworkCore;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

/// <summary>The Products module's data: table <c>prd_Products</c>.</summary>
public sealed class ProductsDbContext(DbContextOptions<ProductsDbContext> options) : ModuleDbContext(options)
{
    /// <inheritdoc />
    public override string TablePrefix => "prd";

    /// <summary>Products.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.Property(p => p.Code).IsRequired().HasMaxLength(ProductLimits.CodeMaxLength);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(ProductLimits.NameMaxLength);
            entity.Property(p => p.UnitPrice).HasPrecision(18, 4);
            entity.Property(p => p.PackageTare).HasPrecision(18, 4);
            entity.Property(p => p.TargetWeight).HasPrecision(18, 4);
            entity.Property(p => p.LowerTolerance).HasPrecision(18, 4);
            entity.Property(p => p.UpperTolerance).HasPrecision(18, 4);
            entity.Property(p => p.UnitCost).HasPrecision(18, 4);
            entity.Property(p => p.SaleUnit).HasConversion<string>().HasMaxLength(16);
            entity.Property(p => p.BarcodePrefix).HasMaxLength(ProductLimits.BarcodePrefixMaxLength);
            entity.Property(p => p.Ingredients).HasMaxLength(ProductLimits.IngredientsMaxLength);
            entity.HasIndex(p => p.Code).IsUnique();
        });
    }
}

/// <summary>Maximum lengths of the text fields of a product.</summary>
public static class ProductLimits
{
    /// <summary>Maximum length of <see cref="Product.Code"/>.</summary>
    public const int CodeMaxLength = 32;

    /// <summary>Maximum length of <see cref="Product.Name"/>.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Maximum length of <see cref="Product.BarcodePrefix"/>.</summary>
    public const int BarcodePrefixMaxLength = 16;

    /// <summary>Maximum length of <see cref="Product.Ingredients"/>.</summary>
    public const int IngredientsMaxLength = 4000;
}

/// <summary>A product card.</summary>
public sealed class Product
{
    /// <summary>Product id.</summary>
    public Guid Id { get; init; }

    /// <summary>PLU code, unique.</summary>
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

    /// <summary>Ingredients; allergens are marked with asterisks.</summary>
    public string? Ingredients { get; set; }

    /// <summary>Whether the product can be used; products are never deleted.</summary>
    public bool IsActive { get; set; }

    /// <summary>How the product is sold and ordered: by weight or by the piece. A piece weighs <see cref="TargetWeight"/>.</summary>
    public SaleUnit SaleUnit { get; set; } = SaleUnit.Kg;

    /// <summary>Cost per kg, for example of a raw material; optional.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>UTC creation time.</summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>UTC time of the last change.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>How a product is sold and ordered.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SaleUnit>))]
public enum SaleUnit
{
    /// <summary>By weight, in kg.</summary>
    Kg,

    /// <summary>By the piece; one piece weighs the product's target weight.</summary>
    Piece,
}
