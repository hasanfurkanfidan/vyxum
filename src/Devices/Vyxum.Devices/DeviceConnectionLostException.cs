namespace Vyxum.Devices;

/// <summary>Thrown when a protocol decides that the link to the device is dead.</summary>
public sealed class DeviceConnectionLostException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public DeviceConnectionLostException()
        : base("The connection to the device was lost.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public DeviceConnectionLostException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public DeviceConnectionLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
