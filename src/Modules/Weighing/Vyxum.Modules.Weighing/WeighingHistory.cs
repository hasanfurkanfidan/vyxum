using Microsoft.EntityFrameworkCore;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Weighing;

/// <summary>
/// Asks the Weighing module for one page of the records saved so far, as a <see cref="WeighingHistoryPage"/> event.
/// A module that starts after records already exist and needs them from the start publishes it (today the paid
/// Reports module, design decision "Geçmiş tartımlar yüklenir"); the Weighing module does not know who asks. The
/// requester asks for the next page after each page that is not the last, so every answer is one short transaction
/// even with hundreds of thousands of records. Only one module may publish an event name, so a second such module would
/// ask through its own event.
/// </summary>
/// <param name="RequestId">Id the requester chooses; repeated on every page of the answer.</param>
/// <param name="AfterRecordId">The page starts after this record id: 0 for the first page, then the last record id of the page before.</param>
/// <param name="Page">The page number to give the answer, from 1.</param>
[IntegrationEvent("weighing.history-requested", 1)]
public sealed record WeighingHistoryRequested(Guid RequestId, long AfterRecordId = 0, int Page = 1) : IIntegrationEvent;

/// <summary>
/// One page of the records saved before a <see cref="WeighingHistoryRequested"/> arrived, oldest first. A separate event
/// from <see cref="WeighingRecorded"/>, so that modules that react to new records (labels, batches) never see history.
/// Records saved later arrive as usual; the same record can come both ways, so a reader keeps one per record id.
/// </summary>
/// <param name="RequestId">The request this page answers.</param>
/// <param name="Page">Page number from 1.</param>
/// <param name="IsLast">True on the last page (also on an empty first page when there are no records).</param>
/// <param name="Records">The records of this page.</param>
[IntegrationEvent("weighing.history-page", 1)]
public sealed record WeighingHistoryPage(Guid RequestId, int Page, bool IsLast, IReadOnlyList<WeighingHistoryRecord> Records) : IIntegrationEvent;

/// <summary>A record in a <see cref="WeighingHistoryPage"/>: what was published when it was saved, and its cancel.</summary>
/// <param name="Recorded">The record as <see cref="WeighingRecorded"/> carried it (<c>LabelRequested</c> is always false).</param>
/// <param name="CancelledAt">UTC time of the cancel, or null when the record is valid.</param>
/// <param name="CancelReason">Reason of the cancel, if any.</param>
public sealed record WeighingHistoryRecord(WeighingRecorded Recorded, DateTime? CancelledAt, string? CancelReason);

/// <summary>Answers <see cref="WeighingHistoryRequested"/> with one page of the records.</summary>
public sealed class WeighingHistoryRequestedHandler(WeighingDbContext db) : IIntegrationEventHandler<WeighingHistoryRequested>
{
    /// <summary>Records per page.</summary>
    public const int PageSize = 500;

    /// <inheritdoc />
    public async Task HandleAsync(WeighingHistoryRequested integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var records = await db.Records.AsNoTracking()
            .Where(r => r.Id > integrationEvent.AfterRecordId)
            .OrderBy(r => r.Id)
            .Take(PageSize + 1)
            .ToListAsync(cancellationToken);
        var items = records.Take(PageSize)
            .Select(r => new WeighingHistoryRecord(
                WeighingMapper.ToRecordedEvent(r),
                r.Status == WeighingStatus.Cancelled ? r.CancelledAtUtc ?? r.CreatedAtUtc : null,
                r.Status == WeighingStatus.Cancelled ? r.CancelReason : null))
            .ToList();
        db.Publish(new WeighingHistoryPage(integrationEvent.RequestId, Math.Max(1, integrationEvent.Page), records.Count <= PageSize, items));
        await db.SaveChangesAsync(cancellationToken);
    }
}
