namespace Vyxum.Devices;

/// <summary>A byte channel to a device (serial port, TCP socket, simulator). Knows nothing about protocols.</summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>A short human-readable description, for example "COM3 9600 8N1" or "10.0.0.5:4001".</summary>
    string Description { get; }

    /// <summary>Opens the connection.</summary>
    Task OpenAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection. Safe to call when it is not open.</summary>
    Task CloseAsync();

    /// <summary>Writes all bytes to the device.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Reads available bytes into <paramref name="buffer"/>. Returns the number of bytes read; 0 means the remote side closed the connection.</summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Drops bytes that were received but not read yet. Best effort.</summary>
    void DiscardInput();
}
