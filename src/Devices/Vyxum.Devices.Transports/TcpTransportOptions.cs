namespace Vyxum.Devices.Transports;

/// <summary>Settings for <see cref="TcpTransport"/>.</summary>
public sealed record TcpTransportOptions
{
    /// <summary>Host name or IP address of the device (or serial-to-network converter).</summary>
    public required string Host { get; init; }

    /// <summary>TCP port.</summary>
    public required int Port { get; init; }

    /// <summary>How long to wait for the connection to be established. Default: 5 seconds.</summary>
    public TimeSpan ConnectTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(5);

    /// <summary>When set, TCP keep-alive probes are sent after this much idle time, so a vanished device is noticed. Default: off.</summary>
    public TimeSpan? KeepAlive { get; init; }
}
