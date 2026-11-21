using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Vyxum.Modules.Products;

/// <summary>A product as returned by the API. Decimals are invariant-culture strings.</summary>
/// <param name="Id">Product id.</param>
/// <param name="Code">PLU code.</param>
/// <param name="Name">Product name.</param>
/// <param name="UnitPrice">Price per kg, e.g. "780.00".</param>
/// <param name="PackageTare">Package tare in kg, e.g. "0.012".</param>
/// <param name="ShelfLifeDays">Shelf life in days.</param>
/// <param name="BarcodePrefix">Barcode prefix.</param>
/// <param name="Ingredients">Ingredients.</param>
/// <param name="TargetWeight">Target net weight in kg, e.g. "0.250".</param>
/// <param name="LowerTolerance">Allowed amount below the target in kg, e.g. "0.005".</param>
/// <param name="UpperTolerance">Allowed amount above the target in kg, e.g. "0.010".</param>
/// <param name="IsActive">Whether the product is active.</param>
/// <param name="UpdatedAt">UTC time of the last change.</param>
public sealed record ProductDto(
    Guid Id,
    string Code,
    string Name,
    string? UnitPrice,
    string? PackageTare,
    int? ShelfLifeDays,
    string? BarcodePrefix,
    string? Ingredients,
    string? TargetWeight,
    string? LowerTolerance,
    string? UpperTolerance,
    bool IsActive,
    DateTime UpdatedAt);

/// <summary>Body of create and update requests. Decimals are invariant-culture strings.</summary>
/// <param name="Code">PLU code.</param>
/// <param name="Name">Product name.</param>
/// <param name="UnitPrice">Price per kg.</param>
/// <param name="PackageTare">Package tare in kg.</param>
/// <param name="ShelfLifeDays">Shelf life in days.</param>
/// <param name="BarcodePrefix">Barcode prefix.</param>
/// <param name="Ingredients">Ingredients.</param>
/// <param name="TargetWeight">Target net weight in kg; optional.</param>
/// <param name="LowerTolerance">Allowed amount below the target in kg; needs a target.</param>
/// <param name="UpperTolerance">Allowed amount above the target in kg; needs a target.</param>
public sealed record ProductInput(
    string? Code,
    string? Name,
    string? UnitPrice,
    string? PackageTare,
    int? ShelfLifeDays,
    string? BarcodePrefix,
    string? Ingredients,
    string? TargetWeight = null,
    string? LowerTolerance = null,
    string? UpperTolerance = null);

/// <summary>Outcome of a product operation: the product, a not-found marker, or validation errors (field name to resx keys).</summary>
public sealed class ProductResult
{
    private ProductResult()
    {
    }

    /// <summary>The product, when the operation succeeded.</summary>
    public ProductDto? Product { get; private init; }

    /// <summary>True when the product id is unknown.</summary>
    public bool NotFound { get; private init; }

    /// <summary>Validation errors keyed by camelCase field name; values are resx keys. Null unless validation failed.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; private init; }

    /// <summary>True when the operation succeeded.</summary>
    public bool Succeeded => Product is not null;

    internal static ProductResult Ok(ProductDto product) => new() { Product = product };

    internal static ProductResult Missing() => new() { NotFound = true };

    internal static ProductResult Invalid(Dictionary<string, string[]> errors) => new() { Errors = errors };
}

/// <summary>Reads and changes products. Every change publishes <see cref="ProductSaved"/> in the same transaction.</summary>
public sealed class ProductService(ProductsDbContext db)
{
    /// <summary>Lists products ordered by code; <paramref name="search"/> matches code or name anywhere, ignoring case.</summary>
    public async Task<IReadOnlyList<ProductDto>> ListAsync(string? search, bool includeInactive, CancellationToken cancellationToken = default)
    {
        IQueryable<Product> query = db.Products.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        var term = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(p => p.Code.ToLower().Contains(term) || p.Name.ToLower().Contains(term));
        }

        var products = await query.OrderBy(p => p.Code).ToListAsync(cancellationToken);
        return [.. products.Select(ToDto)];
    }

    /// <summary>Gets one product, or null when the id is unknown.</summary>
    public async Task<ProductDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return product is null ? null : ToDto(product);
    }

    /// <summary>Creates an active product.</summary>
    public async Task<ProductResult> CreateAsync(ProductInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new Dictionary<string, string[]>();
        var values = Validate(input, errors);
        if (values is null || await CodeTakenAsync(values.Code, except: null, errors, cancellationToken))
        {
            return ProductResult.Invalid(errors);
        }

        var now = DateTime.UtcNow;
        var product = new Product { Id = Guid.NewGuid(), Code = values.Code, Name = values.Name, IsActive = true, CreatedAtUtc = now };
        Apply(product, values, now);
        db.Products.Add(product);
        return await SaveAsync(product, cancellationToken);
    }

    /// <summary>Replaces the editable fields of a product.</summary>
    public async Task<ProductResult> UpdateAsync(Guid id, ProductInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return ProductResult.Missing();
        }

        var errors = new Dictionary<string, string[]>();
        var values = Validate(input, errors);
        if (values is null || await CodeTakenAsync(values.Code, except: id, errors, cancellationToken))
        {
            return ProductResult.Invalid(errors);
        }

        Apply(product, values, DateTime.UtcNow);
        return await SaveAsync(product, cancellationToken);
    }

    /// <summary>Marks a product inactive. Products are never deleted.</summary>
    public Task<ProductResult> DeactivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        SetActiveAsync(id, false, cancellationToken);

    /// <summary>Marks a product active again.</summary>
    public Task<ProductResult> ActivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        SetActiveAsync(id, true, cancellationToken);

    private async Task<ProductResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return ProductResult.Missing();
        }

        product.IsActive = isActive;
        product.UpdatedAtUtc = DateTime.UtcNow;
        return await SaveAsync(product, cancellationToken);
    }

    private async Task<ProductResult> SaveAsync(Product product, CancellationToken cancellationToken)
    {
        db.Publish(new ProductSaved(
            product.Id, product.Code, product.Name, product.UnitPrice, product.PackageTare,
            product.ShelfLifeDays, product.BarcodePrefix, product.IsActive,
            product.TargetWeight, product.LowerTolerance, product.UpperTolerance));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Possibly lost a race against another request that took the same code.
            var code = product.Code;
            var id = product.Id;
            db.ChangeTracker.Clear();
            if (await db.Products.AnyAsync(p => p.Code == code && p.Id != id, cancellationToken))
            {
                return ProductResult.Invalid(new() { ["code"] = ["products.CodeTaken"] });
            }

            throw;
        }

        return ProductResult.Ok(ToDto(product));
    }

    private async Task<bool> CodeTakenAsync(string code, Guid? except, Dictionary<string, string[]> errors, CancellationToken cancellationToken)
    {
        // Compared case-insensitively on every provider, unlike the unique index on SQLite.
        var lower = code.ToLowerInvariant();
        var taken = await db.Products.AnyAsync(p => p.Code.ToLower() == lower && p.Id != except, cancellationToken);
        if (taken)
        {
            errors["code"] = ["products.CodeTaken"];
        }

        return taken;
    }

    private static void Apply(Product product, Values values, DateTime nowUtc)
    {
        product.Code = values.Code;
        product.Name = values.Name;
        product.UnitPrice = values.UnitPrice;
        product.PackageTare = values.PackageTare;
        product.ShelfLifeDays = values.ShelfLifeDays;
        product.BarcodePrefix = values.BarcodePrefix;
        product.Ingredients = values.Ingredients;
        product.TargetWeight = values.TargetWeight;
        product.LowerTolerance = values.LowerTolerance;
        product.UpperTolerance = values.UpperTolerance;
        product.UpdatedAtUtc = nowUtc;
    }

    private static Values? Validate(ProductInput input, Dictionary<string, string[]> errors)
    {
        var code = input.Code?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            errors["code"] = ["products.CodeRequired"];
        }
        else if (code.Length > ProductLimits.CodeMaxLength)
        {
            errors["code"] = ["products.CodeTooLong"];
        }

        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors["name"] = ["products.NameRequired"];
        }
        else if (name.Length > ProductLimits.NameMaxLength)
        {
            errors["name"] = ["products.NameTooLong"];
        }

        var unitPrice = ParseDecimal(input.UnitPrice, "unitPrice", errors);
        var packageTare = ParseDecimal(input.PackageTare, "packageTare", errors);
        var targetWeight = ParseDecimal(input.TargetWeight, "targetWeight", errors);
        var lowerTolerance = ParseDecimal(input.LowerTolerance, "lowerTolerance", errors);
        var upperTolerance = ParseDecimal(input.UpperTolerance, "upperTolerance", errors);
        if (targetWeight is null && !errors.ContainsKey("targetWeight"))
        {
            // Tolerances are amounts around a target; without one they have no meaning.
            if (lowerTolerance is not null)
            {
                errors["lowerTolerance"] = ["products.ToleranceNeedsTarget"];
            }

            if (upperTolerance is not null)
            {
                errors["upperTolerance"] = ["products.ToleranceNeedsTarget"];
            }
        }

        if (input.ShelfLifeDays is < 0)
        {
            errors["shelfLifeDays"] = ["products.NegativeNotAllowed"];
        }

        var barcodePrefix = NullIfBlank(input.BarcodePrefix);
        if (barcodePrefix?.Length > ProductLimits.BarcodePrefixMaxLength)
        {
            errors["barcodePrefix"] = ["products.BarcodePrefixTooLong"];
        }

        var ingredients = NullIfBlank(input.Ingredients);
        if (ingredients?.Length > ProductLimits.IngredientsMaxLength)
        {
            errors["ingredients"] = ["products.IngredientsTooLong"];
        }

        return errors.Count > 0
            ? null
            : new Values(code, name, unitPrice, packageTare, input.ShelfLifeDays, barcodePrefix, ingredients, targetWeight, lowerTolerance, upperTolerance);
    }

    private static decimal? ParseDecimal(string? text, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Strict: optional sign and decimal point only; no thousands separators, exponents or culture formats.
        var trimmed = text.Trim();
        if (!decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || decimal.Round(value, 4) != value
            || Math.Abs(value) >= 100_000_000_000_000m)
        {
            errors[field] = ["products.InvalidNumber"];
            return null;
        }

        if (value < 0 || trimmed.StartsWith('-'))
        {
            errors[field] = ["products.NegativeNotAllowed"];
            return null;
        }

        return value;
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? Format(decimal? value) => value?.ToString("0.00######", CultureInfo.InvariantCulture);

    private static ProductDto ToDto(Product p) => new(
        p.Id, p.Code, p.Name, Format(p.UnitPrice), Format(p.PackageTare), p.ShelfLifeDays,
        p.BarcodePrefix, p.Ingredients, Format(p.TargetWeight), Format(p.LowerTolerance), Format(p.UpperTolerance), p.IsActive, DateTime.SpecifyKind(p.UpdatedAtUtc, DateTimeKind.Utc));

    private sealed record Values(
        string Code,
        string Name,
        decimal? UnitPrice,
        decimal? PackageTare,
        int? ShelfLifeDays,
        string? BarcodePrefix,
        string? Ingredients,
        decimal? TargetWeight,
        decimal? LowerTolerance,
        decimal? UpperTolerance);
}
