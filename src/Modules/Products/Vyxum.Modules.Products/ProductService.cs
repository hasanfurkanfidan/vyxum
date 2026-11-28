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
/// <param name="SaleUnit">How the product is sold and ordered: <c>Kg</c> or <c>Piece</c>.</param>
/// <param name="UnitCost">Cost per kg, e.g. "412.50".</param>
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
    DateTime UpdatedAt,
    SaleUnit SaleUnit = SaleUnit.Kg,
    string? UnitCost = null);

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
/// <param name="SaleUnit">How the product is sold and ordered; null means <c>Kg</c>. <c>Piece</c> needs a target weight, the weight of one piece.</param>
/// <param name="UnitCost">Cost per kg; optional.</param>
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
    string? UpperTolerance = null,
    SaleUnit? SaleUnit = null,
    string? UnitCost = null);

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

/// <summary>What <see cref="ProductService.ReceiveAsync"/> did with a material card from an ERP.</summary>
public enum ReceiveOutcome
{
    /// <summary>A new product card was made.</summary>
    Created,

    /// <summary>The existing product card changed.</summary>
    Updated,

    /// <summary>The product exists and already has these values.</summary>
    Unchanged,

    /// <summary>The product exists and the sender said to leave it.</summary>
    Left,

    /// <summary>The card cannot be a product (no usable code), so nothing was done.</summary>
    Skipped,
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

    /// <summary>
    /// Applies a material card an ERP connector brought in, by product code (compared ignoring case). A missing product is
    /// made; an existing one is changed only when the card says <c>Update</c>, and then only its name, sale unit and
    /// active state: prices, tolerances, labels and everything a person entered stay. Nothing is ever deleted. Does
    /// not throw for card data it cannot use: such a card is <see cref="ReceiveOutcome.Skipped"/>.
    /// </summary>
    public async Task<ReceiveOutcome> ReceiveAsync(ProductReceived received, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(received);

        var code = received.Code?.Trim() ?? string.Empty;
        if (code.Length == 0 || code.Length > ProductLimits.CodeMaxLength)
        {
            return ReceiveOutcome.Skipped;
        }

        var name = received.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = code;
        }
        else if (name.Length > ProductLimits.NameMaxLength)
        {
            name = name[..ProductLimits.NameMaxLength];
        }

        var lower = code.ToLowerInvariant();
        var product = await db.Products.SingleOrDefaultAsync(p => p.Code.ToLower() == lower, cancellationToken);
        var now = DateTime.UtcNow;
        if (product is null)
        {
            // Sold by weight: a piece needs the weight of one piece, which a material card does not give.
            product = new Product { Id = Guid.NewGuid(), Code = code, Name = name, IsActive = received.Active, SaleUnit = SaleUnit.Kg, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Products.Add(product);
            return await SaveReceivedAsync(product, ReceiveOutcome.Created, cancellationToken);
        }

        if (!string.Equals(received.IfExists, "Update", StringComparison.Ordinal))
        {
            return ReceiveOutcome.Left;
        }

        var changed = product.Name != name || product.IsActive != received.Active;
        product.Name = name;
        product.IsActive = received.Active;
        if (SaleUnitOf(received.Unit, product.TargetWeight) is { } saleUnit && saleUnit != product.SaleUnit)
        {
            product.SaleUnit = saleUnit;
            changed = true;
        }

        if (!changed)
        {
            return ReceiveOutcome.Unchanged;
        }

        product.UpdatedAtUtc = now;
        return await SaveReceivedAsync(product, ReceiveOutcome.Updated, cancellationToken);
    }

    private async Task<ReceiveOutcome> SaveReceivedAsync(Product product, ReceiveOutcome outcome, CancellationToken cancellationToken)
    {
        var saved = await SaveAsync(product, cancellationToken);
        // Another request took the code first: fail, so the message is handled again and then finds that product.
        return saved.Succeeded ? outcome : throw new InvalidOperationException("The product code was taken by another request; the message is handled again.");
    }

    /// <summary>
    /// The sale unit an ERP inventory unit stands for, or null when the unit is unknown (the product keeps what it has).
    /// A piece unit counts only for a product that has a target weight, the weight of one piece.
    /// </summary>
    private static SaleUnit? SaleUnitOf(string? unit, decimal? targetWeight)
    {
        var text = unit?.Trim().TrimEnd('.').ToLowerInvariant();
        return text switch
        {
            "kg" or "kgm" or "kilo" or "kilogram" or "kilogramm" => SaleUnit.Kg,
            "ad" or "adet" or "pcs" or "pc" or "piece" or "pieces" or "ea" or "each" or "unit" or "nos" when targetWeight is > 0 => SaleUnit.Piece,
            _ => null,
        };
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

    private static ProductSaved ToSaved(Product product) => new(
        product.Id, product.Code, product.Name, product.UnitPrice, product.PackageTare,
        product.ShelfLifeDays, product.BarcodePrefix, product.IsActive,
        product.TargetWeight, product.LowerTolerance, product.UpperTolerance, product.Ingredients,
        product.SaleUnit.ToString(), product.UnitCost);

    /// <summary>
    /// Publishes <see cref="ProductSaved"/> for every product, a page at a time, through the outbox. Used when another
    /// module asks for the whole catalog (<see cref="CatalogRequested"/>); nothing in the catalog changes. Returns how many were published.
    /// </summary>
    public async Task<int> PublishCatalogAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        while (true)
        {
            // Ordered by code, which is unique, so the pages do not overlap.
            var page = await db.Products.AsNoTracking().OrderBy(p => p.Code).Skip(published).Take(CatalogPageSize).ToListAsync(cancellationToken);
            if (page.Count == 0) return published;
            foreach (var product in page) db.Publish(ToSaved(product));
            await db.SaveChangesAsync(cancellationToken);
            published += page.Count;
        }
    }

    /// <summary>How many products one page of a catalog publication holds.</summary>
    public const int CatalogPageSize = 200;

    private async Task<ProductResult> SaveAsync(Product product, CancellationToken cancellationToken)
    {
        db.Publish(ToSaved(product));
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
        product.SaleUnit = values.SaleUnit;
        product.UnitCost = values.UnitCost;
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

        var unitCost = ParseDecimal(input.UnitCost, "unitCost", errors);
        var saleUnit = input.SaleUnit ?? SaleUnit.Kg;
        if (!Enum.IsDefined(saleUnit))
        {
            errors["saleUnit"] = ["products.InvalidNumber"];
        }
        else if (saleUnit == SaleUnit.Piece && targetWeight is not > 0 && !errors.ContainsKey("targetWeight"))
        {
            // A piece is counted, and its weight is what turns pieces into kg.
            errors["targetWeight"] = ["products.PieceNeedsWeight"];
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
            : new Values(code, name, unitPrice, packageTare, input.ShelfLifeDays, barcodePrefix, ingredients, targetWeight, lowerTolerance, upperTolerance, saleUnit, unitCost);
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
        p.BarcodePrefix, p.Ingredients, Format(p.TargetWeight), Format(p.LowerTolerance), Format(p.UpperTolerance), p.IsActive, DateTime.SpecifyKind(p.UpdatedAtUtc, DateTimeKind.Utc),
        p.SaleUnit, Format(p.UnitCost));

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
        decimal? UpperTolerance,
        SaleUnit SaleUnit,
        decimal? UnitCost);
}
