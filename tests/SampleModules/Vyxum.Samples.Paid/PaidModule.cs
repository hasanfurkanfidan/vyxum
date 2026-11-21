using System.Resources;
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
/// every placed order (event <c>samples.order-placed</c>), has one screen (embedded <c>ui/index.js</c>) and tells whether a device is a licensed station.
/// Test-only, never shipped.
/// </summary>
public sealed class PaidModule : IModule
{
    /// <summary>The module id, which is also its license key.</summary>
    public const string Id = "samples.paid";

    /// <summary>A capability the module provides, available only while it is licensed or in grace.</summary>
    public const string EntriesCapability = "samples.paid.entries";

    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        Id = Id,
        DisplayNameKey = StringsPrefix + ".ModuleName",
        Version = new Version(1, 0),
        Provides = [EntriesCapability],
        Optional = ["samples.orders"],
        RequiresLicense = true,
        Ui = new ModuleUi { Entry = "index.js", SdkVersion = 1 },
    };

    /// <summary>Prefix of the module's UI strings (<c>Resources/Strings.resx</c>).</summary>
    public const string StringsPrefix = "samples-paid";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.AddUiStrings(StringsPrefix, new ResourceManager("Vyxum.Samples.Paid.Resources.Strings", typeof(PaidModule).Assembly));
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
