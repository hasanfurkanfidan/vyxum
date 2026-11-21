using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vyxum.Licensing;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// Holds the license of this installation: reads the stored file from <c>plt_Settings</c>, verifies it with the trusted
/// keys (<see cref="ILicenseKeyRing"/>), evaluates it against the clock and keeps the latest seen time, so that turning
/// the clock back never extends a license. Registered as a singleton and as <see cref="ILicenseState"/>.
/// </summary>
/// <remarks>
/// <para>
/// The evaluation is cached. It is computed again after <see cref="CacheDuration"/>, when the local date changes, when
/// the clock goes back, and at once after this process changes the license or the stations (<see cref="Invalidate"/>).
/// So a change made through another application instance on the same database is seen within
/// <see cref="CacheDuration"/>; a single instance (the normal installation) always sees its own changes immediately.
/// </para>
/// <para>
/// The latest seen time is kept per license id (key <see cref="PlatformSettingKeys.LicenseLastSeenPrefix"/>) and written
/// when it has moved forward by at least <see cref="LastSeenWriteInterval"/>, to keep database writes rare. In memory it
/// never goes back. A clock set far ahead once therefore ends that license early; the support path is a newly issued
/// license, which has a new id.
/// </para>
/// <para>
/// Loading uses synchronous database calls on purpose: it reads a few small rows at most once a minute, and it lets
/// modules read <see cref="ILicenseState"/> without async plumbing. A database error is not hidden: callers fail closed.
/// </para>
/// </remarks>
public sealed partial class LicenseManager : ILicenseState
{
    /// <summary>How long an evaluation is reused.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    /// <summary>The smallest forward move of the latest seen time that is written to the database.</summary>
    public static readonly TimeSpan LastSeenWriteInterval = TimeSpan.FromHours(1);

    /// <summary>How far the clock may be behind the latest seen time before <see cref="LicenseSnapshot.ClockBehind"/> is set.</summary>
    public static readonly TimeSpan ClockBehindTolerance = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LicenseEvaluator _evaluator;
    private readonly TimeProvider _time;
    private readonly LicenseSnapshot.ModuleSet _modules;
    private readonly ILogger<LicenseManager> _logger;
    private readonly Lock _refreshLock = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private LicenseSnapshot? _snapshot;
    private string? _lastSignature;
    private long _generation;
    private long _version;

    /// <summary>Creates the manager.</summary>
    public LicenseManager(
        IServiceScopeFactory scopeFactory,
        ILicenseKeyRing keyRing,
        TimeProvider time,
        ModuleCatalog catalog,
        ILogger<LicenseManager> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(keyRing);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        Reader = new LicenseReader(keyRing);
        _evaluator = new LicenseEvaluator(time);
        _time = time;
        _modules = new LicenseSnapshot.ModuleSet(catalog);
        _logger = logger;
    }

    /// <summary>
    /// A number that changes whenever what the license allows may have changed (an upload, a removal, a station change,
    /// or a re-evaluation with a different result). The outbox dispatcher uses it to release held events.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>The reader that verifies files with the trusted keys.</summary>
    internal LicenseReader Reader { get; }

    /// <inheritdoc />
    public bool HasLicense => GetSnapshot().License is not null;

    /// <inheritdoc />
    public LicenseCondition Status => GetSnapshot().Status;

    /// <inheritdoc />
    public string? LicenseeName => GetSnapshot().License?.Payload.Licensee.Name;

    /// <inheritdoc />
    public LicenseKind? Kind => GetSnapshot().License?.Payload.Type switch
    {
        null => null,
        LicenseType.Trial => LicenseKind.Trial,
        LicenseType.Subscription => LicenseKind.Subscription,
        LicenseType.Perpetual => LicenseKind.Perpetual,
        LicenseType.Nfr => LicenseKind.Nfr,
        var other => throw new InvalidOperationException($"Unknown license type {other}."),
    };

    /// <inheritdoc />
    public int? GraceDaysLeft => GetSnapshot().Evaluation?.GraceDaysLeft;

    /// <inheritdoc />
    public int StationLimit => GetSnapshot().StationLimit;

    /// <inheritdoc />
    public IReadOnlyList<string> LicensedStations => GetSnapshot().Stations;

    /// <inheritdoc />
    public string? RawLicense => GetSnapshot().License?.RawText;

    /// <inheritdoc />
    public ModuleLicenseState GetModuleStatus(string moduleId) => GetSnapshot().GetModuleStatus(moduleId);

    /// <inheritdoc />
    public bool IsStationLicensed(string deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        return GetSnapshot().IsStationLicensed(deviceId);
    }

    /// <summary>The current evaluation, from the cache when it is still fresh.</summary>
    internal LicenseSnapshot GetSnapshot()
    {
        var current = Volatile.Read(ref _snapshot);
        if (current is not null && IsFresh(current, _time.GetUtcNow()))
        {
            return current;
        }

        lock (_refreshLock)
        {
            var now = _time.GetUtcNow();
            current = Volatile.Read(ref _snapshot);
            if (current is not null && IsFresh(current, now))
            {
                return current;
            }

            var generation = Interlocked.Read(ref _generation);
            var fresh = Load(now);

            // A change made while loading (Invalidate) makes this result possibly stale: return it, do not keep it.
            if (Interlocked.Read(ref _generation) == generation)
            {
                Volatile.Write(ref _snapshot, fresh);
            }

            if (!string.Equals(fresh.Signature, _lastSignature, StringComparison.Ordinal))
            {
                _lastSignature = fresh.Signature;
                Interlocked.Increment(ref _version);
                if (fresh.Status == LicenseCondition.Invalid)
                {
                    LogStoredLicenseInvalid(_logger, fresh.ErrorCode);
                }
            }

            return fresh;
        }
    }

    /// <summary>Forgets the cached evaluation; call after changing the stored license or stations.</summary>
    internal void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _snapshot, null);
        Interlocked.Increment(ref _version);
    }

    /// <summary>The settings key of the latest seen time of license <paramref name="licenseId"/>.</summary>
    internal static string LastSeenKey(string licenseId) =>
        PlatformSettingKeys.LicenseLastSeenPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(licenseId)));

    private bool IsFresh(LicenseSnapshot snapshot, DateTimeOffset now) =>
        now >= snapshot.ComputedAt
        && now - snapshot.ComputedAt < CacheDuration
        && snapshot.ComputedForDate == LocalDate(now);

    private DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _time.LocalTimeZone).DateTime);

    private LicenseSnapshot Load(DateTimeOffset now)
    {
        var date = LocalDate(now);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var stored = db.Settings.AsNoTracking()
            .Where(s => s.Key == PlatformSettingKeys.LicenseFile || s.Key == PlatformSettingKeys.LicenseStations)
            .ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (!stored.TryGetValue(PlatformSettingKeys.LicenseFile, out var text))
        {
            return LicenseSnapshot.None(_modules, date, now);
        }

        var read = Reader.Read(text);
        if (!read.IsValid)
        {
            return LicenseSnapshot.Invalid(_modules, read.ErrorCode!, date, now);
        }

        var license = read.License!;
        var licenseId = license.Payload.LicenseId;
        var key = LastSeenKey(licenseId);
        var row = db.Settings.FirstOrDefault(s => s.Key == key);
        var storedSeen = ParseInstant(row?.Value);
        var lastSeen = Later(storedSeen, _seen.TryGetValue(licenseId, out var inMemory) ? inMemory : null);

        var evaluation = _evaluator.Evaluate(license, lastSeen);
        _seen.AddOrUpdate(licenseId, evaluation.LastSeen, (_, old) => old > evaluation.LastSeen ? old : evaluation.LastSeen);
        if (storedSeen is null || evaluation.LastSeen - storedSeen.Value >= LastSeenWriteInterval)
        {
            WriteLastSeen(db, row, key, evaluation.LastSeen, now);
        }

        var stations = LicenseStations.Effective(
            LicenseStations.Parse(stored.GetValueOrDefault(PlatformSettingKeys.LicenseStations)),
            db.Set<DeviceConfig>().AsNoTracking().Select(d => d.Id).ToHashSet(StringComparer.Ordinal),
            license.Payload.Stations);
        var clockBehind = now < evaluation.LastSeen - ClockBehindTolerance;
        return LicenseSnapshot.Verified(_modules, evaluation, clockBehind, stations, date, now);
    }

    private void WriteLastSeen(PlatformDbContext db, PlatformSetting? row, string key, DateTimeOffset lastSeen, DateTimeOffset now)
    {
        var value = lastSeen.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        if (row is null)
        {
            db.Settings.Add(new PlatformSetting { Key = key, Value = value, UpdatedAt = now.UtcDateTime });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = now.UtcDateTime;
        }

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException exception)
        {
            // Another application instance wrote it at the same moment; the in-memory value still protects this one.
            LogLastSeenNotSaved(_logger, exception.Message);
        }
    }

    private static DateTimeOffset? ParseInstant(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The stored license file does not verify ({ErrorCode}); licensed modules stay locked.")]
    private static partial void LogStoredLicenseInvalid(ILogger logger, string? errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The latest seen license time could not be saved: {Error}")]
    private static partial void LogLastSeenNotSaved(ILogger logger, string error);
}
