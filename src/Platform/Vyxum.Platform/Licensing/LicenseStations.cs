using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Vyxum.Platform;

/// <summary>
/// The choice of licensed stations (decision V-49): a JSON array of device ids stored under
/// <see cref="PlatformSettingKeys.LicenseStations"/>.
/// </summary>
internal static class LicenseStations
{
    /// <summary>The stored ids, in stored order; empty when missing or unreadable.</summary>
    public static List<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string?>>(json)?
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The value to store for <paramref name="deviceIds"/>.</summary>
    public static string Serialize(IEnumerable<string> deviceIds) => JsonSerializer.Serialize(deviceIds);

    /// <summary>The stored ids that are still devices, in stored order, at most <paramref name="limit"/>.</summary>
    public static IReadOnlyList<string> Effective(IEnumerable<string> stored, IReadOnlySet<string> existingDevices, int limit) =>
        [.. stored.Where(existingDevices.Contains).Take(Math.Max(limit, 0))];

    /// <summary>
    /// The choice after a license with <paramref name="limit"/> stations is installed: the previous choice, limited to
    /// existing devices and trimmed to the limit; when nothing of it is left, the first enabled devices by creation order.
    /// </summary>
    public static async Task<IReadOnlyList<string>> AssignAsync(PlatformDbContext db, string? storedJson, int limit, CancellationToken cancellationToken)
    {
        var devices = await db.Set<DeviceConfig>().AsNoTracking()
            .Select(d => new { d.Id, d.IsEnabled, d.CreatedAt })
            .ToListAsync(cancellationToken);
        var kept = Effective(Parse(storedJson), devices.Select(d => d.Id).ToHashSet(StringComparer.Ordinal), limit);
        if (kept.Count > 0)
        {
            return kept;
        }

        return
        [
            .. devices
                .Where(d => d.IsEnabled)
                .OrderBy(d => d.CreatedAt)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
                .Select(d => d.Id)
                .Take(Math.Max(limit, 0)),
        ];
    }

    /// <summary>Removes <paramref name="deviceId"/> from the stored choice (tracked change; the caller saves).</summary>
    public static async Task RemoveDeviceAsync(PlatformDbContext db, string deviceId, DateTime now, CancellationToken cancellationToken)
    {
        var row = await db.Settings.SingleOrDefaultAsync(s => s.Key == PlatformSettingKeys.LicenseStations, cancellationToken);
        if (row is null)
        {
            return;
        }

        var ids = Parse(row.Value);
        if (ids.Remove(deviceId))
        {
            row.Value = Serialize(ids);
            row.UpdatedAt = now;
        }
    }
}
