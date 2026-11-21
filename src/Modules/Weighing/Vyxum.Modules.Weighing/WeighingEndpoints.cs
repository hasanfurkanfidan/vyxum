using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Vyxum.Modules.Weighing;

internal static class WeighingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/weighing");

        group.MapGet("/settings", (WeighingQueries queries) => Results.Ok(queries.Settings()));

        group.MapGet("/products", async (WeighingQueries queries, string? search, CancellationToken ct) =>
            Results.Ok(await queries.ProductsAsync(search, ct)));

        group.MapGet("/records", async (
            WeighingQueries queries,
            DateTimeOffset? from,
            DateTimeOffset? to,
            Guid? productId,
            string? lot,
            bool? includeCancelled,
            int? skip,
            int? take,
            CancellationToken ct) =>
        {
            var filter = new WeighingRecordFilter(from?.UtcDateTime, to?.UtcDateTime, productId, lot, includeCancelled ?? true);
            return Results.Ok(await queries.ListAsync(filter, skip, take, ct));
        });

        group.MapGet("/records/export", async (
            HttpContext http,
            WeighingQueries queries,
            WeighingExporter exporter,
            DateTimeOffset? from,
            DateTimeOffset? to,
            Guid? productId,
            string? lot,
            bool? includeCancelled,
            CancellationToken ct) =>
        {
            var filter = new WeighingRecordFilter(from?.UtcDateTime, to?.UtcDateTime, productId, lot, includeCancelled ?? true);
            if (await queries.CountItemsAsync(filter, ct) > WeighingQueries.MaxExportRows)
            {
                return Results.Json(new { code = WeighingErrors.ExportTooLarge }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var file = await exporter.ExportAsync(filter, CultureInfo.CurrentUICulture, ct);
            var today = (http.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetLocalNow();
            return Results.File(
                file,
                WeighingExporter.ContentType,
                $"weighing-records-{today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.xlsx");
        });

        group.MapGet("/records/recent", async (WeighingQueries queries, string? deviceId, int? take, CancellationToken ct) =>
            Results.Ok(await queries.RecentAsync(deviceId, take, ct)));

        group.MapGet("/records/{id:long}", async (WeighingQueries queries, long id, CancellationToken ct) =>
            await queries.GetAsync(id, ct) is { } record ? Results.Ok(record) : Results.NotFound());

        group.MapPost("/records", async (WeighingService service, CreateRecordRequest request, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result is { Record: { } record, Created: true }
                ? Results.Created($"/api/weighing/records/{record.Id}", record)
                : ToHttp(result);
        });

        group.MapPost("/records/{id:long}/cancel", async (WeighingService service, long id, CancelRecordRequest? request, CancellationToken ct) =>
            ToHttp(await service.CancelAsync(id, request, ct)));
    }

    private static IResult ToHttp(WeighingResult result) =>
        result.NotFound ? Results.NotFound()
        : result.Errors is { } errors ? Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value))
        : result.RejectionCode is { } code ? Results.Json(new { code }, statusCode: result.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity)
        : Results.Ok(result.Record);
}
