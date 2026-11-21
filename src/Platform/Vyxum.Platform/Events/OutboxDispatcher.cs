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

    /// <summary>Creates the dispatcher.</summary>
    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ModuleCatalog catalog,
        IOptions<OutboxOptions> options,
        TimeProvider time,
        ILogger<OutboxDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _catalog = catalog;
        _options = options.Value;
        _time = time;
        _logger = logger;

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
            var attempted = 0;
            foreach (var module in _catalog.Modules.Where(m => m.DbContextType is not null))
            {
                attempted += await DispatchModuleAsync(module.DbContextType!, cancellationToken);
            }

            return attempted;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> DispatchModuleAsync(Type dbContextType, CancellationToken cancellationToken)
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
            foreach (var (handler, handlerDbContextType) in _handlers.GetValueOrDefault((message.EventName, message.EventVersion)) ?? [])
            {
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

            message.Attempts++;
            if (errors.Count == 0)
            {
                message.ProcessedAt = UtcNow();
                message.NextAttemptAt = null;
            }
            else
            {
                var error = string.Join(Environment.NewLine, errors);
                message.LastError = error.Length <= OutboxMessage.LastErrorMaxLength ? error : error[..OutboxMessage.LastErrorMaxLength];
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler {HandlerName} failed for event {EventName} v{EventVersion}, message {MessageId}; it will be retried.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string handlerName, string eventName, int eventVersion, Guid messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handler {HandlerName} already processed message {MessageId}; skipped.")]
    private static partial void LogAlreadyHandled(ILogger logger, string handlerName, Guid messageId);
}
