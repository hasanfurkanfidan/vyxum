using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

/// <summary>The product catalog: PLU codes, names, prices and label data.</summary>
public sealed class ProductsModule : IModule
{
    /// <summary>Capability this module provides.</summary>
    public const string CatalogCapability = "products.catalog";

    /// <summary>Permission to add, change, deactivate and activate products.</summary>
    public const string EditPermission = "products.edit";

    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "products",
        DisplayNameKey = ProductsStrings.KeyPrefix + ".ModuleName",
        Version = new Version(1, 0),
        Provides = [CatalogCapability],
        Publishes = [typeof(ProductSaved)],
        Permissions =
        [
            new() { Id = EditPermission, NameKey = ProductsStrings.KeyPrefix + ".PermissionEdit", DefaultRoles = [BuiltInRoles.Quality, BuiltInRoles.RecipeAuthor, BuiltInRoles.Planning] },
        ],
    };

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        context.AddDbContext<ProductsDbContext>();
        context.AddEventHandler<ProductReceived, ProductReceivedHandler>();
        context.AddEventHandler<CatalogRequested, CatalogRequestedHandler>();
        services.AddScoped<ProductService>();
        services.AddUiStrings(ProductsStrings.KeyPrefix, ProductsStrings.ResourceManager);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ProductEndpoints.Map(endpoints);
}
