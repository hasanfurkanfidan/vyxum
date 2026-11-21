using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Weighing;

/// <summary>Filter of the record list. Times are UTC; <see cref="From"/> is inclusive, <see cref="To"/> exclusive.</summary>
/// <param name="From">Records saved at or after this time.</param>
/// <param name="To">Records saved before this time.</param>
/// <param name="ProductId">Only this product.</param>
/// <param name="Lot">Only this lot (exact match).</param>
/// <param name="IncludeCancelled">Include cancelled records in the items (totals never sum them).</param>
public sealed record WeighingRecordFilter(DateTime? From, DateTime? To, Guid? ProductId, string? Lot, bool IncludeCancelled = true);

/// <summary>Reads records, totals, the product copy and the settings.</summary>
public sealed class WeighingQueries(WeighingDbContext db, IOptions<WeighingOptions> options, ICapabilityRegistry capabilities)
{
    /// <summary>Largest page of <see cref="ListAsync"/>.</summary>
    public const int MaxTake = 500;

    /// <summary>Default page of <see cref="ListAsync"/>.</summary>
    public const int DefaultTake = 100;

    /// <summary>Default number of <see cref="RecentAsync"/>.</summary>
    public const int DefaultRecentTake = 10;

    /// <summary>Gets one record, or null.</summary>
    public async Task<WeighingRecordDto?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var record = await db.Records.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return record is null ? null : WeighingMapper.ToDto(record);
    }

    /// <summary>Lists filtered records newest first, with the totals of every filtered record.</summary>
    public async Task<WeighingRecordListDto> ListAsync(WeighingRecordFilter filter, int? skip, int? take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var records = await ItemsQuery(filter)
            .OrderByDescending(r => r.Id)
            .Skip(Math.Max(skip ?? 0, 0))
            .Take(Math.Clamp(take ?? DefaultTake, 0, MaxTake))
            .ToListAsync(cancellationToken);

        var totals = await TotalsAsync(filter, cancellationToken);
        return new WeighingRecordListDto([.. records.Select(WeighingMapper.ToDto)], totals);
    }

    /// <summary>Largest number of records <c>GET /api/weighing/records/export</c> writes; a larger result is rejected.</summary>
    public const int MaxExportRows = 100_000;

    /// <summary>Totals of every filtered record (cancelled ones are counted, never summed).</summary>
    internal async Task<WeighingTotalsDto> TotalsAsync(WeighingRecordFilter filter, CancellationToken cancellationToken = default) =>
        await TotalsAsync(Filter(db.Records.AsNoTracking(), filter), cancellationToken);

    /// <summary>Number of records <see cref="ItemsQuery"/> returns.</summary>
    internal Task<int> CountItemsAsync(WeighingRecordFilter filter, CancellationToken cancellationToken = default) =>
        ItemsQuery(filter).CountAsync(cancellationToken);

    /// <summary>Streams the filtered records oldest first, without paging.</summary>
    internal IAsyncEnumerable<WeighingRecord> StreamItems(WeighingRecordFilter filter) =>
        ItemsQuery(filter).OrderBy(r => r.Id).AsAsyncEnumerable();

    /// <summary>The latest records, optionally of one device, newest first.</summary>
    public async Task<IReadOnlyList<WeighingRecordDto>> RecentAsync(string? deviceId, int? take, CancellationToken cancellationToken = default)
    {
        IQueryable<WeighingRecord> query = db.Records.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var id = deviceId.Trim();
            query = query.Where(r => r.DeviceId == id);
        }

        var records = await query
            .OrderByDescending(r => r.Id)
            .Take(Math.Clamp(take ?? DefaultRecentTake, 0, MaxTake))
            .ToListAsync(cancellationToken);
        return [.. records.Select(WeighingMapper.ToDto)];
    }

    /// <summary>Active products of the local copy ordered by code; <paramref name="search"/> matches code or name anywhere, ignoring case.</summary>
    public async Task<IReadOnlyList<WeighingProductDto>> ProductsAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);
        var term = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(p => p.Code.ToLower().Contains(term) || p.Name.ToLower().Contains(term));
        }

        var products = await query.OrderBy(p => p.Code).ToListAsync(cancellationToken);
        return [.. products.Select(p => new WeighingProductDto(
            p.ProductId,
            p.Code,
            p.Name,
            WeighingMapper.FormatPrice(p.UnitPrice),
            WeighingMapper.FormatPrice(p.PackageTare),
            p.ShelfLifeDays,
            p.BarcodePrefix,
            WeighingMapper.FormatPrice(p.TargetWeight),
            WeighingMapper.FormatPrice(p.LowerTolerance),
            WeighingMapper.FormatPrice(p.UpperTolerance)))];
    }

    /// <summary>The rules and the optional capabilities the screen needs.</summary>
    public WeighingSettingsDto Settings()
    {
        var o = options.Value;
        return new WeighingSettingsDto(
            o.RequireStable,
            WeightUnits.TrimScale(o.MinimumNet).ToString(System.Globalization.CultureInfo.InvariantCulture),
            o.RequireCancelReason,
            o.Currency.Trim(),
            o.AmountDecimals,
            CanPrintLabel: capabilities.IsAvailable(WeighingModule.LabelsCapability),
            ShowOperator: capabilities.IsAvailable(WeighingModule.UsersCapability),
            ProductsAvailable: capabilities.IsAvailable(WeighingModule.ProductsCapability));
    }

    /// <summary>The records the list and the export show: the filter, and without cancelled ones unless asked.</summary>
    private IQueryable<WeighingRecord> ItemsQuery(WeighingRecordFilter filter)
    {
        var filtered = Filter(db.Records.AsNoTracking(), filter);
        return filter.IncludeCancelled ? filtered : filtered.Where(r => r.Status == WeighingStatus.Saved);
    }

    private static IQueryable<WeighingRecord> Filter(IQueryable<WeighingRecord> query, WeighingRecordFilter filter)
    {
        if (filter.From is { } from)
        {
            var fromUtc = ToUtc(from);
            query = query.Where(r => r.CreatedAtUtc >= fromUtc);
        }

        if (filter.To is { } to)
        {
            var toUtc = ToUtc(to);
            query = query.Where(r => r.CreatedAtUtc < toUtc);
        }

        if (filter.ProductId is { } productId)
        {
            query = query.Where(r => r.ProductId == productId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Lot))
        {
            var lot = filter.Lot.Trim();
            query = query.Where(r => r.Lot == lot);
        }

        return query;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value,
    };

    private static async Task<WeighingTotalsDto> TotalsAsync(IQueryable<WeighingRecord> filtered, CancellationToken cancellationToken)
    {
        var cancelledCount = await filtered.CountAsync(r => r.Status == WeighingStatus.Cancelled, cancellationToken);
        var saved = filtered.Where(r => r.Status == WeighingStatus.Saved);

        // Exact integer sums in SQL on every provider: net per (unit, decimals), amount per decimals.
        var netGroups = await saved
            .GroupBy(r => new { r.Unit, r.GrossScale, r.TareScale })
            .Select(g => new { g.Key.Unit, g.Key.GrossScale, g.Key.TareScale, Count = g.Count(), Sum = g.Sum(r => r.NetUnits) })
            .ToListAsync(cancellationToken);
        var amountGroups = await saved
            .Where(r => r.AmountMinor != null)
            .GroupBy(r => r.AmountScale)
            .Select(g => new { Scale = g.Key, Sum = g.Sum(r => r.AmountMinor) ?? 0 })
            .ToListAsync(cancellationToken);

        var net = netGroups
            .Select(g => new { g.Unit, g.Count, Scale = Math.Max(g.GrossScale, g.TareScale), g.Sum })
            .GroupBy(g => g.Unit)
            .Select(u =>
            {
                var scale = u.Max(g => g.Scale);
                var value = u.Sum(g => g.Sum / WeightUnits.Pow10(g.Scale));
                return new NetTotalDto(u.Key, WeightUnits.Format(value, scale));
            })
            .OrderBy(t => t.Unit, StringComparer.Ordinal)
            .ToList();

        string? amount = null;
        if (amountGroups.Count > 0)
        {
            var scale = amountGroups.Max(g => g.Scale);
            amount = WeightUnits.Format(amountGroups.Sum(g => g.Sum / WeightUnits.Pow10(g.Scale)), scale);
        }

        return new WeighingTotalsDto(netGroups.Sum(g => g.Count), cancelledCount, net, amount);
    }
}
