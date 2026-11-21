using System.Text.Json;

namespace Vyxum.Platform.Abstractions;

/// <summary>The JSON format of integration events in the outbox (System.Text.Json, web defaults: camelCase).</summary>
public static class IntegrationEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes an event using its runtime type.</summary>
    public static string Serialize(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), Options);
    }

    /// <summary>Deserializes <paramref name="payload"/> into <paramref name="eventType"/>.</summary>
    public static IIntegrationEvent Deserialize(string payload, Type eventType) =>
        JsonSerializer.Deserialize(payload, eventType, Options) as IIntegrationEvent
        ?? throw new JsonException($"Payload could not be read as '{eventType.FullName}'.");
}
