using System.Text.Json.Serialization;

namespace Vyxum.Platform.Abstractions;

/// <summary>Names of the commands a device can carry out, as listed in <see cref="LiveDevice.SupportedCommands"/>.</summary>
public static class DeviceCommandNames
{
    /// <summary>Set the zero point of the scale.</summary>
    public const string Zero = "Zero";
}

/// <summary>The outcome of a device command.</summary>
public enum DeviceCommandOutcome
{
    /// <summary>The device carried out the command.</summary>
    Done,

    /// <summary>The device or its protocol does not support the command.</summary>
    NotSupported,

    /// <summary>The device answered but refused the command (for example the weight is in motion or out of range).</summary>
    Rejected,

    /// <summary>The device or its agent is not connected, or did not answer in time.</summary>
    Unavailable,
}

/// <summary>The result of a device command.</summary>
/// <param name="Result">The outcome.</param>
/// <param name="Detail">A short English explanation for logs; null when there is nothing to add. Not meant for display.</param>
public sealed record DeviceCommandResult(
    DeviceCommandOutcome Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null);

/// <summary>
/// Sends commands to devices through the agent that serves them. Only commands the operator asks for explicitly are
/// sent, each exactly once per call (decision V-29). Readings are not affected by these calls.
/// </summary>
public interface IDeviceCommands
{
    /// <summary>Asks the device to set its zero point (MT-SICS <c>Z</c>). Never throws for device or agent problems.</summary>
    Task<DeviceCommandResult> ZeroAsync(string deviceId, CancellationToken cancellationToken = default);
}
