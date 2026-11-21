using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Samples.Paid;

/// <summary>
/// Sample module that requires a license (<see cref="ModuleManifest.RequiresLicense"/>). It keeps entries, adds one for
/// every placed order (event <c>samples.order-placed</c>) and tells whether a device is a licensed station.
/// Test-only, never shipped.
/// </summary>
public sealed class PaidModule : IModule
{
    /// <summary>The module id, which is also its license key.</summary>
    public const string Id = "samples.paid";

    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        Id = Id,
        Version = new Version(1, 0),
        Optional = ["samples.orders"],
        RequiresLicense = true,
    };

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        context.AddDbContext<PaidDbContext>();
        context.AddEventHandler<OrderPlaced, OrderPlacedHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/samples-paid");
        group.MapGet("/entries", async (PaidDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Entries.OrderBy(e => e.CreatedAt).Select(e => e.Text).ToListAsync(ct)));
        group.MapPost("/entries", async (EntryInput input, PaidDbContext db, CancellationToken ct) =>
        {
            db.Entries.Add(new Entry { Id = Guid.NewGuid(), Text = input.Text, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            return Results.Created();
        });
        group.MapGet("/stations/{deviceId}", (string deviceId, ILicenseState license) =>
            Results.Ok(new { licensed = license.IsStationLicensed(deviceId), licensee = license.LicenseeName }));

        // A hub, to show that the license gate covers every kind of endpoint (the host registers SignalR).
        endpoints.MapHub<PaidHub>("/hubs/samples-paid");
    }

    /// <summary>Body of <c>POST /api/samples-paid/entries</c>.</summary>
    /// <param name="Text">The entry text.</param>
    public sealed record EntryInput(string Text);
}

/// <summary>An empty hub of the paid sample module.</summary>
public sealed class PaidHub : Hub;
