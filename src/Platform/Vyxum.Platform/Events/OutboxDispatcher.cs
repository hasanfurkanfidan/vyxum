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
/// period; incoming events are kept, not dropped), the handler is not called: the message stays pending with
/// <see cref="OutboxMessage.LastError"/> starting with <see cref="LicenseWaitPrefix"/> and is tried again after
/// <see cref="OutboxOptions.LicenseWaitDelay"/>, without counting an attempt. Waiting messages are released at once when
/// <see cref="LicenseManager.Version"/> changes (a license uploaded or removed in this process, or a new evaluation
/// result), so they are handled as soon as the module is licensed. Other handlers of the same message run as usual;
/// the inbox keeps them from running twice.
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
    private readonly bool _hasLicensedHandlers;
    private long? _releasedLicenseVersion;

    /// <summary>The start of <see cref="OutboxMessage.LastError"/> of a message that waits for a module's license.</summary>
    public const string LicenseWaitPrefix = "Waiting for a license: ";

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
        _hasLicensedHandlers = catalog.Modules.Any(m => m.Manifest.RequiresLicense && m.EventHandlers.Count > 0);

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
            if (_hasLicensedHandlers)
            {
                licenses = _licenses.GetSnapshot();
                var version = _licenses.Version;
                if (version != _releasedLicenseVersion)
                {
                    await ReleaseLicenseWaitsAsync(cancellationToken);
                    _releasedLicenseVersion = version;
                }
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

    /// <summary>Makes every message that waits for a license due now, in every module's outbox.</summary>
    private async Task ReleaseLicenseWaitsAsync(CancellationToken cancellationToken)
    {
        foreach (var module in _catalog.Modules.Where(m => m.DbContextType is not null))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(module.DbContextType!);
            await db.OutboxMessages
                .Where(m => m.ProcessedAt == null && m.NextAttemptAt != null && m.LastError != null && m.LastError.StartsWith(LicenseWaitPrefix))
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.NextAttemptAt, (DateTime?)null), cancellationToken);
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
                // Not an attempt: nothing failed, the module is only waiting for its license.
                message.LastError = Truncate(LicenseWaitPrefix + string.Join(", ", waiting.Distinct(StringComparer.Ordinal)));
                message.NextAttemptAt = UtcNow() + _options.LicenseWaitDelay;
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handler {HandlerName} already processed message {MessageId}; skipped.")]
    private static partial void LogAlreadyHandled(ILogger logger, string handlerName, Guid messageId);
}
