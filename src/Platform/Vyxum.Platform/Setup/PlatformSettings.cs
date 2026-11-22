using Microsoft.EntityFrameworkCore;

namespace Vyxum.Platform;

/// <summary>Keys of the settings the platform itself stores in <see cref="IPlatformSettings"/>.</summary>
public static class PlatformSettingKeys
{
    /// <summary>UTC time the setup wizard was completed, ISO 8601 (round-trip format). Missing until then.</summary>
    public const string SetupCompletedAt = "setup.completedAt";

    /// <summary>The company name entered in the setup wizard.</summary>
    public const string CompanyName = "company.name";

    /// <summary>The installed license file (<c>.vxlic</c>) exactly as uploaded. Missing in the free version.</summary>
    public const string LicenseFile = "license.file";

    /// <summary>The device ids chosen as licensed stations, a JSON array of strings (decision V-49).</summary>
    public const string LicenseStations = "license.stations";

    /// <summary>
    /// Prefix of the latest time seen for one license (ISO 8601, round-trip), followed by the lowercase hex SHA-256 of
    /// the license id. Kept per license, and kept when the license is removed, so that turning the clock back never
    /// extends a license; a newly issued license (new id) starts fresh.
    /// </summary>
    public const string LicenseLastSeenPrefix = "license.lastSeen.";
}

/// <summary>Platform-wide text settings stored in the database (<c>plt_Settings</c>). Scoped.</summary>
public interface IPlatformSettings
{
    /// <summary>Maximum length of a value.</summary>
    const int MaxValueLength = 4000;

    /// <summary>The value of <paramref name="key"/>, or null when it is not set.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Sets <paramref name="key"/>; a null value removes it.</summary>
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);
}

internal sealed class PlatformSettings(PlatformDbContext db, TimeProvider time) : IPlatformSettings
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await db.Settings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (value is { Length: > IPlatformSettings.MaxValueLength })
        {
            throw new ArgumentException($"A setting value is at most {IPlatformSettings.MaxValueLength} characters.", nameof(value));
        }

        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (value is null)
        {
            if (existing is not null)
            {
                db.Settings.Remove(existing);
            }
        }
        else if (existing is null)
        {
            db.Settings.Add(new PlatformSetting { Key = key, Value = value, UpdatedAt = time.GetUtcNow().UtcDateTime });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = time.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
