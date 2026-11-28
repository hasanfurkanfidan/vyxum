using Microsoft.Extensions.Logging;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Products;

/// <summary>
/// Creates or updates the product card of a material card an ERP connector brought in. The platform's inbox keeps a
/// message from being handled twice; handling the same data again changes nothing anyway.
/// </summary>
public sealed partial class ProductReceivedHandler(ProductService products, ILogger<ProductReceivedHandler> logger) : IIntegrationEventHandler<ProductReceived>
{
    /// <inheritdoc />
    public async Task HandleAsync(ProductReceived integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(context);

        var outcome = await products.ReceiveAsync(integrationEvent, cancellationToken);
        if (outcome == ReceiveOutcome.Skipped)
        {
            // Retrying would not make the card usable; it waits for the next read of the ERP, after the code is fixed there.
            LogSkipped(logger, integrationEvent.ErpItemCode, integrationEvent.ConnectionId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ERP item {ErpItemCode} of connection {ConnectionId} has no usable product code or name and was not brought in.")]
    private static partial void LogSkipped(ILogger logger, string erpItemCode, Guid connectionId);
}
