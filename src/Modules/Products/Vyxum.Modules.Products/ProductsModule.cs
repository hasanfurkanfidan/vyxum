using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

/// <summary>The product catalog: PLU codes, names, prices and label data.</summary>
public sealed class ProductsModule : IModule
{
    /// <summary>Capability this module provides.</summary>
    public const string CatalogCapability = "products.catalog";

    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "products",
        DisplayNameKey = ProductsStrings.KeyPrefix + ".ModuleName",
        Version = new Version(1, 0),
        Provides = [CatalogCapability],
        Publishes = [typeof(ProductSaved)],
    };

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        context.AddDbContext<ProductsDbContext>();
        services.AddScoped<ProductService>();
        services.AddUiStrings(ProductsStrings.KeyPrefix, ProductsStrings.ResourceManager);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ProductEndpoints.Map(endpoints);
}
