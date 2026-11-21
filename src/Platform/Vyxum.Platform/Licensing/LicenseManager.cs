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
/// Reading never writes. <see cref="GetSnapshot"/> (and so every <see cref="ILicenseState"/> member) only reads a few
/// small rows, so it is safe on any thread, also inside a module's open write transaction. The latest seen time is
/// written by <see cref="LicenseMaintenanceService"/> alone (<see cref="PersistLastSeenAsync"/>): about once an hour,
/// after an upload and at shutdown. It is kept per license id (key <see cref="PlatformSettingKeys.LicenseLastSeenPrefix"/>).
/// </para>
/// <para>
/// Clock rule: the evaluation uses <c>max(now, latest seen)</c>. The latest seen time moves forward with the clock, but
/// a jump of more than <see cref="ForwardJumpTolerance"/> past it is not accepted at once: while it lasts
/// <see cref="LicenseSnapshot.ClockAhead"/> is set and the evaluation still uses the (later) clock, and only when the
/// clock has stayed that far ahead for <see cref="ForwardJumpConfirmation"/> of process time (monotonic, not the wall
/// clock) is the new time accepted and later persisted. So one wrong reading of the clock (a bad battery, a wrong
/// time server) does not end a license for good; a clock that really moved on is accepted after a few hours.
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

    /// <summary>How far the clock may be behind the latest seen time before <see cref="LicenseSnapshot.ClockBehind"/> is set.</summary>
    public static readonly TimeSpan ClockBehindTolerance = TimeSpan.FromDays(1);

    /// <summary>The largest forward move of the clock past the latest seen time that is accepted at once.</summary>
    public static readonly TimeSpan ForwardJumpTolerance = TimeSpan.FromDays(2);

    /// <summary>How long (process time) a larger forward jump must be observed before it is accepted.</summary>
    public static readonly TimeSpan ForwardJumpConfirmation = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LicenseEvaluator _evaluator;
    private readonly TimeProvider _time;
    private readonly LicenseSnapshot.ModuleSet _modules;
    private readonly ILogger<LicenseManager> _logger;
    private readonly Lock _refreshLock = new();
    private readonly SemaphoreSlim _persistRequested = new(0, 1);

    // Per license id; every field is read and written under _refreshLock.
    private readonly ConcurrentDictionary<string, SeenTime> _seen = new(StringComparer.Ordinal);
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
    /// Raised after what the license allows may have changed: an upload, a removal, a station change, or a new
    /// evaluation with a different result. Handlers run on the caller's thread and must be quick; read the new state
    /// through this manager.
    /// </summary>
    internal event Action? Changed;

    /// <summary>
    /// A number that changes whenever what the license allows may have changed (an upload, a removal, a station change,
    /// or a re-evaluation with a different result).
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

    /// <summary>The current evaluation, from the cache when it is still fresh. Reads only; never writes.</summary>
    internal LicenseSnapshot GetSnapshot()
    {
        var current = Volatile.Read(ref _snapshot);
        if (current is not null && IsFresh(current, _time.GetUtcNow()))
        {
            return current;
        }

        var changed = false;
        LicenseSnapshot fresh;
        lock (_refreshLock)
        {
            var now = _time.GetUtcNow();
            current = Volatile.Read(ref _snapshot);
            if (current is not null && IsFresh(current, now))
            {
                return current;
            }

            var generation = Interlocked.Read(ref _generation);
            fresh = Load(now);

            // A change made while loading (Invalidate) makes this result possibly stale: return it, do not keep it.
            if (Interlocked.Read(ref _generation) == generation)
            {
                Volatile.Write(ref _snapshot, fresh);
            }

            if (!string.Equals(fresh.Signature, _lastSignature, StringComparison.Ordinal))
            {
                _lastSignature = fresh.Signature;
                Interlocked.Increment(ref _version);
                changed = true;
                if (fresh.Status == LicenseCondition.Invalid)
                {
                    LogStoredLicenseInvalid(_logger, fresh.ErrorCode);
                }
            }
        }

        if (changed)
        {
            RaiseChanged();
        }

        return fresh;
    }

    /// <summary>Forgets the cached evaluation; call after changing the stored license or stations.</summary>
    internal void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _snapshot, null);
        Interlocked.Increment(ref _version);
        RaiseChanged();
    }

    /// <summary>Asks <see cref="LicenseMaintenanceService"/> to persist the latest seen time soon (after an upload).</summary>
    internal void RequestPersist()
    {
        if (_persistRequested.CurrentCount == 0)
        {
            try
            {
                _persistRequested.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already requested.
            }
        }
    }

    /// <summary>Waits until <see cref="RequestPersist"/> is called or <paramref name="timeout"/> passes; true when requested.</summary>
    internal Task<bool> WaitForPersistRequestAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _persistRequested.WaitAsync(timeout, cancellationToken);

    /// <summary>
    /// Writes the accepted latest seen time of every license this process has evaluated, where it is later than the
    /// stored one. Called only by <see cref="LicenseMaintenanceService"/> (and tests), never from a request or a handler.
    /// </summary>
    internal async Task PersistLastSeenAsync(CancellationToken cancellationToken)
    {
        foreach (var (licenseId, seen) in _seen)
        {
            DateTimeOffset? accepted, stored;
            lock (_refreshLock)
            {
                accepted = seen.Accepted;
                stored = seen.Stored;
            }

            if (accepted is null || (stored is not null && accepted <= stored))
            {
                continue;
            }

            var key = LastSeenKey(licenseId);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            var current = ParseInstant(row?.Value);
            if (current is null || current < accepted)
            {
                var value = accepted.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
                var now = _time.GetUtcNow().UtcDateTime;
                if (row is null)
                {
                    db.Settings.Add(new PlatformSetting { Key = key, Value = value, UpdatedAt = now });
                }
                else
                {
                    row.Value = value;
                    row.UpdatedAt = now;
                }

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    current = accepted;
                }
                catch (DbUpdateException exception)
                {
                    // Another application instance wrote it at the same moment; the next run tries again.
                    LogLastSeenNotSaved(_logger, exception.Message);
                    continue;
                }
            }

            lock (_refreshLock)
            {
                seen.Stored = Later(seen.Stored, current);
            }
        }
    }

    /// <summary>The settings key of the latest seen time of license <paramref name="licenseId"/>.</summary>
    internal static string LastSeenKey(string licenseId) =>
        PlatformSettingKeys.LicenseLastSeenPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(licenseId)));

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception exception)
        {
            LogChangedHandlerFailed(_logger, exception);
        }
    }

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
        var storedSeen = ParseInstant(db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefault());

        var seen = _seen.GetOrAdd(licenseId, _ => new SeenTime());
        seen.Stored = Later(seen.Stored, storedSeen);
        var clockAhead = Observe(seen, now);

        var evaluation = _evaluator.Evaluate(license, seen.Accepted);
        var stations = LicenseStations.Effective(
            LicenseStations.Parse(stored.GetValueOrDefault(PlatformSettingKeys.LicenseStations)),
            db.Set<DeviceConfig>().AsNoTracking().Select(d => d.Id).ToHashSet(StringComparer.Ordinal),
            license.Payload.Stations);
        var clockBehind = seen.Accepted is { } accepted && now < accepted - ClockBehindTolerance;
        return LicenseSnapshot.Verified(_modules, evaluation, clockBehind, clockAhead, stations, date, now);
    }

    /// <summary>
    /// Applies the clock rule (see the remarks) to <paramref name="seen"/> at <paramref name="now"/> and returns whether
    /// the clock is ahead by an unconfirmed jump. Called under <see cref="_refreshLock"/>.
    /// </summary>
    private bool Observe(SeenTime seen, DateTimeOffset now)
    {
        var latest = Later(seen.Accepted, seen.Stored);
        seen.Accepted = latest;
        if (latest is null || now - latest.Value <= ForwardJumpTolerance)
        {
            // First sight, the clock moving on normally, or the clock behind (the latest seen time stays).
            if (latest is null || now > latest.Value)
            {
                seen.Accepted = now;
            }

            seen.JumpObservedSince = null;
            return false;
        }

        seen.JumpObservedSince ??= _time.GetTimestamp();
        if (_time.GetElapsedTime(seen.JumpObservedSince.Value) >= ForwardJumpConfirmation)
        {
            LogForwardJumpAccepted(_logger, latest.Value, now);
            seen.Accepted = now;
            seen.JumpObservedSince = null;
            return false;
        }

        return true;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The clock moved from {LatestSeen:O} to {Now:O} and stayed there; the new time is accepted for the license.")]
    private static partial void LogForwardJumpAccepted(ILogger logger, DateTimeOffset latestSeen, DateTimeOffset now);

    [LoggerMessage(Level = LogLevel.Error, Message = "A license change handler failed.")]
    private static partial void LogChangedHandlerFailed(ILogger logger, Exception exception);

    /// <summary>The latest seen time of one license, as this process knows it.</summary>
    private sealed class SeenTime
    {
        /// <summary>The value in the database, as last read or written.</summary>
        public DateTimeOffset? Stored { get; set; }

        /// <summary>The latest accepted time: never goes back; persisted by <see cref="PersistLastSeenAsync"/>.</summary>
        public DateTimeOffset? Accepted { get; set; }

        /// <summary>Monotonic timestamp of the first observation of the current unconfirmed forward jump.</summary>
        public long? JumpObservedSince { get; set; }
    }
}
