using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Vyxum.Devices.Simulator;

/// <summary>
/// Hosts a simulated device on a TCP port (loopback by default), so the real <c>TcpTransport</c> can be used against it.
/// Every client connection gets its own session of the device.
/// </summary>
public sealed class SimulatorTcpServer : IAsyncDisposable
{
    private readonly ISimulatedDevice _device;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<TcpClient, Task> _clients = new();
    private Task? _acceptLoop;
    private int _accepted;

    /// <summary>Creates the server. Port 0 picks a free port; read it from <see cref="Port"/> after <see cref="Start"/>.</summary>
    public SimulatorTcpServer(ISimulatedDevice device, int port = 0, IPAddress? address = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _listener = new TcpListener(address ?? IPAddress.Loopback, port);
    }

    /// <summary>The port the server listens on. Valid after <see cref="Start"/>.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>How many connections were accepted so far.</summary>
    public int AcceptedConnections => Volatile.Read(ref _accepted);

    /// <summary>How many connections are open now.</summary>
    public int ActiveConnections => _clients.Count;

    /// <summary>Starts listening. Returns the server for chaining.</summary>
    public SimulatorTcpServer Start()
    {
        if (_acceptLoop is not null)
        {
            throw new InvalidOperationException("The server has already been started.");
        }

        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
        return this;
    }

    /// <summary>Closes all open connections (the device vanishes); new connections are still accepted.</summary>
    public void DisconnectClients()
    {
        foreach (var client in _clients.Keys)
        {
            client.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        DisconnectClients();
        try
        {
            if (_acceptLoop is not null)
            {
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            await Task.WhenAll(_clients.Values).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Shutting down: sessions that fail or hang are abandoned.
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            Interlocked.Increment(ref _accepted);
            client.NoDelay = true;
            _clients[client] = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            await _device.RunSessionAsync(client.GetStream(), _cts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A simulated device that fails, or a client that vanishes, just ends the session.
        }
        finally
        {
            client.Dispose();
            _clients.TryRemove(client, out _);
        }
    }
}
