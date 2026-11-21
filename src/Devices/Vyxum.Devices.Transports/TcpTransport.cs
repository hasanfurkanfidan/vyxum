using System.Net.Sockets;

namespace Vyxum.Devices.Transports;

/// <summary>An <see cref="ITransport"/> over a TCP connection (network scale or serial-to-network converter).</summary>
public sealed class TcpTransport : ITransport
{
    private readonly TcpTransportOptions _options;
    private TcpClient? _client;
    private NetworkStream? _stream;

    /// <summary>Creates the transport. The connection is made by <see cref="OpenAsync"/>.</summary>
    public TcpTransport(TcpTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string Description => $"{_options.Host}:{_options.Port}";

    /// <inheritdoc />
    /// <exception cref="TimeoutException">The connection was not established within the connect timeout.</exception>
    /// <exception cref="SocketException">The connection was refused or the host was not found.</exception>
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("The connection is already open.");
        }

        var client = new TcpClient { NoDelay = true };
        try
        {
            if (_options.KeepAlive is { } keepAlive)
            {
                var seconds = Math.Max(1, (int)keepAlive.TotalSeconds);
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, seconds);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, seconds);
            }

            using var timeout = new CancellationTokenSource(_options.ConnectTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await client.ConnectAsync(_options.Host, _options.Port, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Could not connect to {Description} within {_options.ConnectTimeout.TotalSeconds:0.##} seconds.");
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _stream = client.GetStream();
    }

    /// <inheritdoc />
    public Task CloseAsync()
    {
        var client = Interlocked.Exchange(ref _client, null);
        _stream = null;
        client?.Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        await RequireStream().WriteAsync(buffer, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        await RequireStream().ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public void DiscardInput()
    {
        var stream = _stream;
        if (stream is null)
        {
            return;
        }

        try
        {
            var scratch = new byte[1024];
            while (stream.DataAvailable)
            {
                if (stream.Read(scratch, 0, scratch.Length) == 0)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Best effort: a dead connection will fail on the next read or write.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    private NetworkStream RequireStream() =>
        _stream ?? throw new InvalidOperationException("The connection is not open.");
}
