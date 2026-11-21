namespace Vyxum.Platform.Abstractions;

/// <summary>
/// Marker for events that cross module boundaries. Every implementation needs an
/// <see cref="IntegrationEventAttribute"/> giving its name and version. Events are serialized
/// with System.Text.Json, so modules may each keep their own copy of an event contract.
/// </summary>
public interface IIntegrationEvent
{
}
