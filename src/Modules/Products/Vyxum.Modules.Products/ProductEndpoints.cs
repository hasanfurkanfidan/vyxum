using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

internal static class ProductEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/products");
        var edit = endpoints.MapGroup("/api/products").AddEndpointFilter(RequireEdit);

        group.MapGet("/", async (ProductService products, string? search, bool? includeInactive, CancellationToken ct) =>
            Results.Ok(await products.ListAsync(search, includeInactive ?? false, ct)));

        group.MapGet("/{id:guid}", async (ProductService products, Guid id, CancellationToken ct) =>
            await products.GetAsync(id, ct) is { } product ? Results.Ok(product) : Results.NotFound());

        edit.MapPost("/", async (ProductService products, ProductInput input, CancellationToken ct) =>
        {
            var result = await products.CreateAsync(input, ct);
            return result.Succeeded
                ? Results.Created($"/api/products/{result.Product!.Id}", result.Product)
                : ToHttp(result);
        });

        edit.MapPut("/{id:guid}", async (ProductService products, Guid id, ProductInput input, CancellationToken ct) =>
            ToHttp(await products.UpdateAsync(id, input, ct)));

        edit.MapPost("/{id:guid}/deactivate", async (ProductService products, Guid id, CancellationToken ct) =>
            ToHttp(await products.DeactivateAsync(id, ct)));

        edit.MapPost("/{id:guid}/activate", async (ProductService products, Guid id, CancellationToken ct) =>
            ToHttp(await products.ActivateAsync(id, ct)));
    }

    /// <summary>Changes need <c>products.edit</c>; without a users module everybody has it.</summary>
    private static async ValueTask<object?> RequireEdit(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.RequestServices.GetRequiredService<IAuthorization>().IsAllowed(ProductsModule.EditPermission)
            ? await next(context)
            : Results.Json(new { code = ProductsStrings.KeyPrefix + ".NotAllowed" }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ToHttp(ProductResult result) =>
        result.NotFound ? Results.NotFound()
        : result.Errors is { } errors ? Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value))
        : Results.Ok(result.Product);
}
