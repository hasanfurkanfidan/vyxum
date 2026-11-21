using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Vyxum.Modules.Products;

internal static class ProductEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/products");

        group.MapGet("/", async (ProductService products, string? search, bool? includeInactive, CancellationToken ct) =>
            Results.Ok(await products.ListAsync(search, includeInactive ?? false, ct)));

        group.MapGet("/{id:guid}", async (ProductService products, Guid id, CancellationToken ct) =>
            await products.GetAsync(id, ct) is { } product ? Results.Ok(product) : Results.NotFound());

        group.MapPost("/", async (ProductService products, ProductInput input, CancellationToken ct) =>
        {
            var result = await products.CreateAsync(input, ct);
            return result.Succeeded
                ? Results.Created($"/api/products/{result.Product!.Id}", result.Product)
                : ToHttp(result);
        });

        group.MapPut("/{id:guid}", async (ProductService products, Guid id, ProductInput input, CancellationToken ct) =>
            ToHttp(await products.UpdateAsync(id, input, ct)));

        group.MapPost("/{id:guid}/deactivate", async (ProductService products, Guid id, CancellationToken ct) =>
            ToHttp(await products.DeactivateAsync(id, ct)));

        group.MapPost("/{id:guid}/activate", async (ProductService products, Guid id, CancellationToken ct) =>
            ToHttp(await products.ActivateAsync(id, ct)));
    }

    private static IResult ToHttp(ProductResult result) =>
        result.NotFound ? Results.NotFound()
        : result.Errors is { } errors ? Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value))
        : Results.Ok(result.Product);
}
