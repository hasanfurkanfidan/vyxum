using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// Delivers pending outbox messages of every module to the registered handlers.
/// </summary>
/// <remarks>
/// Each handler runs in its own scope and in a transaction of its module's DbContext: the inbox is
/// checked for (MessageId, HandlerName), the handler runs, the inbox row is added and everything is
/// committed together. So a handler's effect happens once even when a message is delivered again
/// (for example after a crash before the message was marked processed). When every handler succeeded
/// the message is marked processed; otherwise <see cref="OutboxMessage.Attempts"/>,
/// <see cref="OutboxMessage.LastError"/> and <see cref="OutboxMessage.NextAttemptAt"/> (exponential
/// backoff) are updated and only the failed handlers run again. Messages without handlers are marked
/// processed. Delivery order is best effort by publish time. Designed for a single application
/// instance: several dispatchers on one database are not coordinated.
/// <para>
/// License gate: the handlers of a module that requires a license run only while the module is licensed or in its
/// grace period. When the module is locked, and also when it is read-only (no new data is created after the grace
/// period; incoming events are kept, not dropped), that handler is held: the message's other handlers run as usual
/// (the inbox records each), and the message is parked with <see cref="OutboxMessage.NextAttemptAt"/> =
/// <see cref="HeldUntil"/> and <see cref="OutboxMessage.LastError"/> naming the waiting modules (<see cref="HoldError"/>),
/// without counting an attempt. A held message is never polled, so held work costs nothing while the license does not
/// change and does not delay other messages. When a module becomes able to run (a license is uploaded or renewed, in
/// this process or seen through another instance), its held messages are released with one set-based update per
/// outbox (no provider-specific SQL) and delivered again; handlers that already ran are skipped through the inbox, a
/// module that is still waiting holds the message again.
/// </para>
/// <para>
/// Held events are replayed in full on renewal, not dropped: data completeness matters more (for example stock that
/// must count every weighing). Every event carries its own time (<see cref="IntegrationEventContext.OccurredAt"/>), so
/// how to treat an old event (book it, or only record it) is the paid module's decision, not the platform's.
/// </para>
/// </remarks>
public sealed partial class OutboxDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ModuleCatalog _catalog;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly Dictionary<(string Name, int Version), List<(EventHandlerRegistration Handler, Type DbContextType)>> _handlers;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LicenseManager _licenses;
    private readonly string[] _licensedHandlerModules;
    private HashSet<string>? _runnableModules;

    /// <summary>The start of <see cref="OutboxMessage.LastError"/> of a message that waits for a module's license.</summary>
    public const string LicenseWaitPrefix = "Waiting for a license: ";

    /// <summary>The <see cref="OutboxMessage.NextAttemptAt"/> of a held message: never due by time, only released.</summary>
    public static readonly DateTime HeldUntil = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Creates the dispatcher.</summary>
    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ModuleCatalog catalog,
        IOptions<OutboxOptions> options,
        TimeProvider time,
        LicenseManager licenses,
        ILogger<OutboxDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(licenses);

        _scopeFactory = scopeFactory;
        _catalog = catalog;
        _options = options.Value;
        _time = time;
        _licenses = licenses;
        _logger = logger;
        _licensedHandlerModules = [.. catalog.Modules.Where(m => m.Manifest.RequiresLicense && m.EventHandlers.Count > 0).Select(m => m.Manifest.Id)];

        // The catalog is validated: every handled event has an attribute and every handler module a DbContext.
        _handlers = catalog.Modules
            .SelectMany(m => m.EventHandlers.Select(h => (Handler: h, DbContextType: m.DbContextType!)))
            .GroupBy(h =>
            {
                var attribute = IntegrationEventAttribute.Find(h.Handler.EventType)!;
                return (attribute.Name, attribute.Version);
            })
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Delivers every message that is due now, module by module, and returns how many messages were attempted.
    /// Called periodically by the background service; tests call it directly.
    /// </summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LicenseSnapshot? licenses = null;
            if (_licensedHandlerModules.Length > 0)
            {
                licenses = _licenses.GetSnapshot();
                var runnable = _licensedHandlerModules.Where(id => MayHandle(licenses, id)).ToHashSet(StringComparer.Ordinal);

                // After a start every runnable module is released once: a license may have arrived while stopped.
                var released = runnable.Where(id => _runnableModules?.Contains(id) != true).ToList();
                if (released.Count > 0)
                {
                    await ReleaseHeldAsync(released, cancellationToken);
                }

                _runnableModules = runnable;
            }

            var attempted = 0;
            foreach (var module in _catalog.Modules.Where(m => m.DbContextType is not null))
            {
                attempted += await DispatchModuleAsync(module.DbContextType!, licenses, cancellationToken);
            }

            return attempted;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The <see cref="OutboxMessage.LastError"/> of a message held for <paramref name="moduleIds"/>.</summary>
    /// <remarks>Each id is enclosed in <c>|</c>, so that a release can match one id exactly (module ids never contain it).</remarks>
    internal static string HoldError(IEnumerable<string> moduleIds) =>
        LicenseWaitPrefix + "|" + string.Join("|", moduleIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) + "|";

    /// <summary>Makes the messages held for any of <paramref name="moduleIds"/> due now, in every module's outbox.</summary>
    private async Task ReleaseHeldAsync(IReadOnlyList<string> moduleIds, CancellationToken cancellationToken)
    {
        foreach (var module in _catalog.Modules.Where(m => m.DbContextType is not null))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(module.DbContextType!);
            foreach (var moduleId in moduleIds)
            {
                var marker = "|" + moduleId + "|";
                var released = await db.OutboxMessages
                    .Where(m => m.ProcessedAt == null && m.NextAttemptAt == HeldUntil && m.LastError != null && m.LastError.Contains(marker))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.NextAttemptAt, (DateTime?)null), cancellationToken);
                if (released > 0)
                {
                    LogReleased(_logger, released, moduleId);
                }
            }
        }
    }

    /// <summary>Whether the handlers of <paramref name="moduleId"/> may run under <paramref name="licenses"/>.</summary>
    private static bool MayHandle(LicenseSnapshot? licenses, string moduleId) =>
        licenses is null
        || licenses.GetModuleStatus(moduleId) is ModuleLicenseState.Free or ModuleLicenseState.Licensed or ModuleLicenseState.Grace;

    private async Task<int> DispatchModuleAsync(Type dbContextType, LicenseSnapshot? licenses, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(dbContextType);

        var now = UtcNow();
        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            var errors = new List<string>();
            var waiting = new List<string>();
            foreach (var (handler, handlerDbContextType) in _handlers.GetValueOrDefault((message.EventName, message.EventVersion)) ?? [])
            {
                if (!MayHandle(licenses, handler.ModuleId))
                {
                    waiting.Add(handler.ModuleId);
                    continue;
                }

                try
                {
                    await HandleAsync(message, handler, handlerDbContextType, cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    LogHandlerFailed(_logger, exception, handler.HandlerName, message.EventName, message.EventVersion, message.Id);
                    errors.Add($"{handler.HandlerName}: {exception.GetType().Name}: {exception.Message}");
                }
            }

            if (errors.Count == 0 && waiting.Count > 0)
            {
                // Not an attempt: nothing failed, the waiting modules only need a license. Parked until released.
                message.LastError = Truncate(HoldError(waiting));
                message.NextAttemptAt = HeldUntil;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            message.Attempts++;
            if (errors.Count == 0)
            {
                message.ProcessedAt = UtcNow();
                message.NextAttemptAt = null;
                if (message.LastError?.StartsWith(LicenseWaitPrefix, StringComparison.Ordinal) == true)
                {
                    message.LastError = null;
                }
            }
            else
            {
                message.LastError = Truncate(string.Join(Environment.NewLine, errors));
                message.NextAttemptAt = UtcNow() + RetryBackoff.GetDelay(message.Attempts, _options.RetryBaseDelay, _options.RetryMaxDelay);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return messages.Count;
    }

    private async Task HandleAsync(OutboxMessage message, EventHandlerRegistration handler, Type dbContextType, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        // The handler resolves the same scoped DbContext, so its writes join this transaction.
        var db = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(dbContextType);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var handlerName = handler.HandlerName;
        if (await db.InboxMessages.AnyAsync(i => i.MessageId == message.Id && i.HandlerName == handlerName, cancellationToken))
        {
            LogAlreadyHandled(_logger, handlerName, message.Id);
            return;
        }

        var integrationEvent = IntegrationEventSerializer.Deserialize(message.Payload, handler.EventType);
        var context = new IntegrationEventContext(message.Id, message.OccurredAt, message.EventName, message.EventVersion);
        await handler.Invoke(scope.ServiceProvider, integrationEvent, context, cancellationToken);

        db.InboxMessages.Add(new InboxMessage { MessageId = message.Id, HandlerName = handlerName, ProcessedAt = UtcNow() });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;

    private static string Truncate(string error) =>
        error.Length <= OutboxMessage.LastErrorMaxLength ? error : error[..OutboxMessage.LastErrorMaxLength];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler {HandlerName} failed for event {EventName} v{EventVersion}, message {MessageId}; it will be retried.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string handlerName, string eventName, int eventVersion, Guid messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Count} held messages for module {ModuleId}, which may run now.")]
    private static partial void LogReleased(ILogger logger, int count, string moduleId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handler {HandlerName} already processed message {MessageId}; skipped.")]
    private static partial void LogAlreadyHandled(ILogger logger, string handlerName, Guid messageId);
}
