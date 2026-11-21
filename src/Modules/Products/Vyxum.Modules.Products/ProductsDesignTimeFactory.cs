using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

/// <summary>Lets <c>dotnet ef</c> create <see cref="ProductsDbContext"/> for either provider.</summary>
internal sealed class ProductsDesignTimeFactory : DesignTimeDbContextFactory<ProductsDbContext>;
