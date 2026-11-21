using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Vyxum.Licensing;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>Maps the license API.</summary>
/// <remarks>
/// The free version has no sign-in (decision V-03), so these endpoints are open; the Users module (phase F6) puts them
/// behind a permission. Errors are problem details (400) with a <c>code</c>: the reader's codes for a rejected file
/// (<c>invalid_format</c>, <c>unsupported_format</c>, <c>unknown_key</c>, <c>invalid_signature</c>,
/// <c>invalid_payload</c>, <c>too_large</c>), and <c>no_license</c>, <c>unknown_device</c>,
/// <c>station_limit_exceeded</c>, <c>stations_too_long</c> for the station choice.
/// </remarks>
public static class LicenseEndpointExtensions
{
    /// <summary>Path of the license endpoint.</summary>
    public const string LicensePath = "/api/license";

    /// <summary>Path of the station choice endpoint.</summary>
    public const string StationsPath = "/api/license/stations";

    /// <summary>The most bytes an upload may have: the character limit in the widest UTF-8 encoding plus a byte order mark.</summary>
    private const int MaxUploadBytes = (LicenseReader.MaxLength * 4) + 3;

    /// <summary>
    /// Maps <c>GET /api/license</c> (the state, see <see cref="LicenseResponse"/>), <c>POST /api/license</c> (upload the
    /// file as the raw request body or as the first file of a multipart form; verified before anything is stored; on
    /// success the stations are chosen and the new state is returned), <c>DELETE /api/license</c> (removes the license
    /// and the station choice; the latest seen time stays) and <c>PUT /api/license/stations</c> with
    /// <c>{ "deviceIds": ["..."] }</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapVyxumLicense(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(LicensePath, (LicenseManager licenses, ModuleCatalog catalog) => Current(licenses, catalog));
        endpoints.MapPost(LicensePath, UploadAsync);
        endpoints.MapDelete(LicensePath, DeleteAsync);
        endpoints.MapPut(StationsPath, SetStationsAsync);
        return endpoints;
    }

    private static IResult Current(LicenseManager licenses, ModuleCatalog catalog) =>
        Results.Json(LicenseResponse.From(licenses.GetSnapshot(), catalog), DeviceEndpointExtensions.JsonOptions);

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        LicenseManager licenses,
        ModuleCatalog catalog,
        PlatformDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadUploadAsync(context.Request, cancellationToken);
        var read = bytes is null
            ? null
            : licenses.Reader.Read(bytes);
        if (read is not { IsValid: true })
        {
            return LicenseProblems.Create(StatusCodes.Status400BadRequest, read?.ErrorCode ?? "too_large");
        }

        var license = read.License!;
        var now = time.GetUtcNow().UtcDateTime;
        var storedStations = await db.Settings.AsNoTracking()
            .Where(s => s.Key == PlatformSettingKeys.LicenseStations)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        var stations = await LicenseStations.AssignAsync(db, storedStations, license.Payload.Stations, cancellationToken);
        var stationsJson = LicenseStations.Serialize(stations);
        if (stationsJson.Length > IPlatformSettings.MaxValueLength)
        {
            // Only device ids at their maximum length with a very large station limit could get here.
            stationsJson = LicenseStations.Serialize([]);
        }

        await SetAsync(db, PlatformSettingKeys.LicenseFile, license.RawText, now, cancellationToken);
        await SetAsync(db, PlatformSettingKeys.LicenseStations, stationsJson, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        licenses.Invalidate();
        return Current(licenses, catalog);
    }

    private static async Task<IResult> DeleteAsync(LicenseManager licenses, PlatformDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Settings
            .Where(s => s.Key == PlatformSettingKeys.LicenseFile || s.Key == PlatformSettingKeys.LicenseStations)
            .ToListAsync(cancellationToken);
        db.Settings.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        licenses.Invalidate();
        return Results.NoContent();
    }

    private static async Task<IResult> SetStationsAsync(
        StationsInput? input,
        LicenseManager licenses,
        ModuleCatalog catalog,
        PlatformDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var license = licenses.GetSnapshot().License;
        if (license is null)
        {
            return LicenseProblems.Create(StatusCodes.Status400BadRequest, "no_license");
        }

        var requested = input?.DeviceIds ?? [];
        var devices = await db.Set<DeviceConfig>().AsNoTracking().Select(d => d.Id).ToListAsync(cancellationToken);
        var known = devices.ToHashSet(StringComparer.Ordinal);
        if (requested.Any(id => id is null || !known.Contains(id)))
        {
            return LicenseProblems.Create(StatusCodes.Status400BadRequest, "unknown_device");
        }

        var ids = requested.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count > license.Payload.Stations)
        {
            return LicenseProblems.Create(StatusCodes.Status400BadRequest, "station_limit_exceeded");
        }

        var json = LicenseStations.Serialize(ids);
        if (json.Length > IPlatformSettings.MaxValueLength)
        {
            return LicenseProblems.Create(StatusCodes.Status400BadRequest, "stations_too_long");
        }

        await SetAsync(db, PlatformSettingKeys.LicenseStations, json, time.GetUtcNow().UtcDateTime, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        licenses.Invalidate();
        return Current(licenses, catalog);
    }

    /// <summary>The uploaded bytes, or null when the upload is larger than any license file can be.</summary>
    private static async Task<byte[]?> ReadUploadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(
                new FormOptions { MultipartBodyLengthLimit = MaxUploadBytes * 2L, ValueLengthLimit = MaxUploadBytes },
                cancellationToken);
            var file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null)
            {
                return [];
            }

            if (file.Length > MaxUploadBytes)
            {
                return null;
            }

            await using var fileStream = file.OpenReadStream();
            return await ReadLimitedAsync(fileStream, cancellationToken);
        }

        return await ReadLimitedAsync(request.Body, cancellationToken);
    }

    private static async Task<byte[]?> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaxUploadBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, count);
        }

        return buffer.ToArray();
    }

    private static async Task SetAsync(PlatformDbContext db, string key, string value, DateTime now, CancellationToken cancellationToken)
    {
        var row = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (row is null)
        {
            db.Settings.Add(new PlatformSetting { Key = key, Value = value, UpdatedAt = now });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = now;
        }
    }

    /// <summary>The body of <c>PUT /api/license/stations</c>.</summary>
    /// <param name="DeviceIds">The device ids to license, at most the station limit; duplicates are ignored.</param>
    internal sealed record StationsInput(IReadOnlyList<string?>? DeviceIds);
}

/// <summary>The body of <c>GET /api/license</c> (and of a successful upload or station change).</summary>
/// <param name="HasLicense">Whether a license file is stored.</param>
/// <param name="Valid">Whether the stored file verifies with a trusted key.</param>
/// <param name="Status"><c>none</c>, <c>invalid</c>, <c>notYetValid</c>, <c>valid</c>, <c>grace</c> or <c>readOnly</c>.</param>
/// <param name="Error">The error code of a stored file that does not verify, for example <c>invalid_signature</c>.</param>
/// <param name="LicenseId">The license id.</param>
/// <param name="Type"><c>trial</c>, <c>subscription</c>, <c>perpetual</c> or <c>nfr</c>.</param>
/// <param name="Licensee">The licensee.</param>
/// <param name="ValidFrom">First valid day (local date).</param>
/// <param name="ValidUntil">Last valid day (local date); null for a perpetual license.</param>
/// <param name="GraceEndsOn">Last day of the grace period; null for a perpetual license.</param>
/// <param name="GraceDaysLeft">During the grace period: days left, counting today.</param>
/// <param name="ClockBehind">True when the computer's clock is more than a day behind a time already seen for this license.</param>
/// <param name="StationLimit">Number of licensed stations; 0 without a valid license.</param>
/// <param name="Stations">The device ids chosen as licensed stations.</param>
/// <param name="Modules">The modules in the license and the installed modules that require one, ordered by id.</param>
internal sealed record LicenseResponse(
    bool HasLicense,
    bool Valid,
    string Status,
    string? Error,
    string? LicenseId,
    string? Type,
    LicenseResponse.LicenseeInfo? Licensee,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    DateOnly? GraceEndsOn,
    int? GraceDaysLeft,
    bool ClockBehind,
    int StationLimit,
    IReadOnlyList<string> Stations,
    IReadOnlyList<LicenseResponse.ModuleInfo> Modules)
{
    public static LicenseResponse From(LicenseSnapshot snapshot, ModuleCatalog catalog)
    {
        var payload = snapshot.License?.Payload;
        var installed = catalog.Modules.ToDictionary(m => m.Manifest.Id, m => m.Manifest, StringComparer.Ordinal);
        var moduleIds = (payload?.Modules ?? [])
            .Concat(catalog.Modules.Where(m => m.Manifest.RequiresLicense).Select(m => m.Manifest.Id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var modules = moduleIds
            .Select(id => new ModuleInfo(
                id,
                installed.ContainsKey(id),
                payload?.Modules.Contains(id, StringComparer.Ordinal) ?? false,
                LicenseText.Of(snapshot.GetModuleStatus(id))))
            .ToList();

        return new LicenseResponse(
            snapshot.HasStoredFile,
            snapshot.License is not null,
            LicenseText.Of(snapshot.Status),
            snapshot.ErrorCode,
            payload?.LicenseId,
            payload is null ? null : LicenseText.Of(payload.Type),
            payload is null ? null : new LicenseeInfo(payload.Licensee.Name, payload.Licensee.TaxNumber),
            payload?.ValidFrom,
            payload?.ValidUntil,
            snapshot.Evaluation?.GraceEndsOn,
            snapshot.Evaluation?.GraceDaysLeft,
            snapshot.ClockBehind,
            snapshot.StationLimit,
            snapshot.Stations,
            modules);
    }

    /// <summary>The licensee.</summary>
    /// <param name="Name">Company or person name.</param>
    /// <param name="TaxNumber">Optional tax number.</param>
    internal sealed record LicenseeInfo(string Name, string? TaxNumber);

    /// <summary>One module of the license view.</summary>
    /// <param name="Id">Module id.</param>
    /// <param name="Installed">Whether the module is loaded in this application.</param>
    /// <param name="Licensed">Whether the license names the module.</param>
    /// <param name="License"><c>free</c>, <c>licensed</c>, <c>grace</c>, <c>readOnly</c> or <c>locked</c>.</param>
    internal sealed record ModuleInfo(string Id, bool Installed, bool Licensed, string License);
}

/// <summary>The JSON text of license states.</summary>
internal static class LicenseText
{
    public static string Of(LicenseCondition status) => status switch
    {
        LicenseCondition.None => "none",
        LicenseCondition.Invalid => "invalid",
        LicenseCondition.NotYetValid => "notYetValid",
        LicenseCondition.Valid => "valid",
        LicenseCondition.Grace => "grace",
        LicenseCondition.ReadOnly => "readOnly",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string Of(ModuleLicenseState state) => state switch
    {
        ModuleLicenseState.Free => "free",
        ModuleLicenseState.Licensed => "licensed",
        ModuleLicenseState.Grace => "grace",
        ModuleLicenseState.ReadOnly => "readOnly",
        ModuleLicenseState.Locked => "locked",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string Of(LicenseType type) => type switch
    {
        LicenseType.Trial => "trial",
        LicenseType.Subscription => "subscription",
        LicenseType.Perpetual => "perpetual",
        LicenseType.Nfr => "nfr",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
