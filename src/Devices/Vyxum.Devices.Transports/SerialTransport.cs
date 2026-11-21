using System.IO.Ports;

namespace Vyxum.Devices.Transports;

/// <summary>An <see cref="ITransport"/> over a serial port (COM port or USB serial adapter).</summary>
public sealed class SerialTransport : ITransport
{
    // How long to wait between checks for incoming bytes. Small enough to be invisible, large enough not to burn CPU.
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(5);

    private readonly SerialTransportOptions _options;
    private SerialPort? _port;

    /// <summary>Creates the transport. The port is opened by <see cref="OpenAsync"/>.</summary>
    public SerialTransport(SerialTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string Description
    {
        get
        {
            var parity = _options.Parity switch
            {
                SerialParity.None => 'N',
                SerialParity.Odd => 'O',
                SerialParity.Even => 'E',
                SerialParity.Mark => 'M',
                _ => 'S',
            };
            var stop = _options.StopBits switch
            {
                SerialStopBits.One => "1",
                SerialStopBits.OnePointFive => "1.5",
                _ => "2",
            };
            return $"{_options.PortName} {_options.BaudRate} {_options.DataBits}{parity}{stop}";
        }
    }

    /// <inheritdoc />
    /// <exception cref="UnauthorizedAccessException">The port is used by another program.</exception>
    /// <exception cref="IOException">The port does not exist or cannot be opened.</exception>
    public Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_port is not null)
        {
            throw new InvalidOperationException("The serial port is already open.");
        }

        var port = new SerialPort(_options.PortName, _options.BaudRate, ToSystem(_options.Parity), _options.DataBits, ToSystem(_options.StopBits))
        {
            Handshake = ToSystem(_options.Handshake),
            DtrEnable = _options.DtrEnable,
            // RTS cannot be set by hand while the handshake drives it.
            RtsEnable = _options.RtsEnable && _options.Handshake is not (SerialHandshake.RequestToSend or SerialHandshake.RequestToSendXOnXOff),
        };
        try
        {
            port.Open();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            port.Dispose();
            if (ex is ArgumentException or InvalidOperationException)
            {
                throw new IOException($"The serial port {_options.PortName} cannot be opened: {ex.Message}", ex);
            }

            throw;
        }

        _port = port;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CloseAsync()
    {
        var port = Interlocked.Exchange(ref _port, null);
        if (port is not null)
        {
            try
            {
                port.Close();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                // The adapter may already be gone; there is nothing left to close.
            }

            port.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var port = RequirePort();
        try
        {
            await port.BaseStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await port.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            throw new IOException($"The serial port {_options.PortName} failed while writing: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cancellation of <c>SerialPort.BaseStream.ReadAsync</c> is not reliable across platforms and drivers, so the token is
    /// never handed to a pending read. Instead the method polls <c>BytesToRead</c> with a short delay (which observes the token
    /// itself) and only calls the stream when data is already waiting, so that read returns at once. This makes cancellation
    /// prompt everywhere. An unplugged adapter makes <c>BytesToRead</c> or the read throw, and that is reported as an
    /// <see cref="IOException"/> so the caller reconnects.
    /// </remarks>
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var port = RequirePort();
        if (buffer.IsEmpty)
        {
            return 0;
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var available = port.BytesToRead;
                if (available > 0)
                {
                    var read = await port.BaseStream.ReadAsync(buffer[..Math.Min(available, buffer.Length)], CancellationToken.None).ConfigureAwait(false);
                    if (read > 0)
                    {
                        return read;
                    }
                }

                await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or ObjectDisposedException)
        {
            throw new IOException($"The serial port {_options.PortName} failed while reading: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public void DiscardInput()
    {
        try
        {
            _port?.DiscardInBuffer();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Best effort: a dead port will fail on the next read or write.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    private SerialPort RequirePort() =>
        _port ?? throw new InvalidOperationException("The serial port is not open.");

    private static Parity ToSystem(SerialParity value) => value switch
    {
        SerialParity.None => Parity.None,
        SerialParity.Odd => Parity.Odd,
        SerialParity.Even => Parity.Even,
        SerialParity.Mark => Parity.Mark,
        SerialParity.Space => Parity.Space,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static StopBits ToSystem(SerialStopBits value) => value switch
    {
        SerialStopBits.One => StopBits.One,
        SerialStopBits.OnePointFive => StopBits.OnePointFive,
        SerialStopBits.Two => StopBits.Two,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static Handshake ToSystem(SerialHandshake value) => value switch
    {
        SerialHandshake.None => Handshake.None,
        SerialHandshake.XOnXOff => Handshake.XOnXOff,
        SerialHandshake.RequestToSend => Handshake.RequestToSend,
        SerialHandshake.RequestToSendXOnXOff => Handshake.RequestToSendXOnXOff,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
