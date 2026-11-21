using System.Threading.Channels;

namespace Vyxum.Devices.Simulator;

/// <summary>
/// An <see cref="ITransport"/> that is an in-memory duplex pipe to a simulated device. Every open starts a new session
/// of the device. The same instance can be closed and opened again, so a factory may return it every time.
/// </summary>
/// <remarks>
/// Fault injection: <see cref="FailNextOpens"/> makes the next opens throw <see cref="IOException"/>;
/// <see cref="Disconnect"/> drops the link (pending and later reads return 0, like a closed socket, and writes throw
/// <see cref="IOException"/>); <see cref="GoSilent"/> makes the device's output disappear while the link stays up.
/// </remarks>
public sealed class SimulatedTransport : ITransport
{
    private readonly ISimulatedDevice _device;
    private readonly Lock _lock = new();
    private Session? _session;
    private int _failOpens;
    private volatile bool _silent;
    private int _openCount;

    /// <summary>Creates a transport to <paramref name="device"/>.</summary>
    public SimulatedTransport(ISimulatedDevice device, string description = "simulator")
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _device = device;
        Description = description;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>True while a session is open (a disconnected link counts as open until it is closed).</summary>
    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                return _session is not null;
            }
        }
    }

    /// <summary>How many opens succeeded.</summary>
    public int OpenCount => Volatile.Read(ref _openCount);

    /// <summary>True while the device's output is dropped.</summary>
    public bool IsSilent => _silent;

    /// <summary>The next <paramref name="count"/> calls to <see cref="OpenAsync"/> throw <see cref="IOException"/>.</summary>
    public void FailNextOpens(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Volatile.Write(ref _failOpens, count);
    }

    /// <summary>Drops the link now. Reads return 0 and writes throw <see cref="IOException"/> until the transport is closed and opened again.</summary>
    public void Disconnect()
    {
        Session? session;
        lock (_lock)
        {
            session = _session;
        }

        session?.Break();
    }

    /// <summary>Drops (or, with false, delivers again) everything the device writes. Commands are still delivered to the device.</summary>
    public void GoSilent(bool silent = true) => _silent = silent;

    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            if (_session is not null)
            {
                throw new InvalidOperationException("The connection is already open.");
            }

            if (Volatile.Read(ref _failOpens) > 0)
            {
                Interlocked.Decrement(ref _failOpens);
                throw new IOException($"Simulated open failure ({Description}).");
            }

            _session = new Session(_device, () => _silent);
            Interlocked.Increment(ref _openCount);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CloseAsync()
    {
        Session? session;
        lock (_lock)
        {
            session = _session;
            _session = null;
        }

        if (session is not null)
        {
            await session.StopAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        RequireSession().Host.WriteAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        RequireSession().Host.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public void DiscardInput()
    {
        Session? session;
        lock (_lock)
        {
            session = _session;
        }

        session?.Host.DiscardPending();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    private Session RequireSession()
    {
        lock (_lock)
        {
            return _session ?? throw new InvalidOperationException("The connection is not open.");
        }
    }

    private sealed class Session
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Channel<byte[]> _toDevice;
        private readonly Channel<byte[]> _toHost;
        private readonly Task _task;

        public Session(ISimulatedDevice device, Func<bool> isSilent)
        {
            var (host, deviceSide, toDevice, toHost) = DuplexStream.CreatePair(isSilent);
            Host = host;
            _toDevice = toDevice;
            _toHost = toHost;
            _task = Task.Run(async () =>
            {
                try
                {
                    await device.RunSessionAsync(deviceSide, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A simulated device that fails ends its session; the host sees a closed connection.
                }
                finally
                {
                    await deviceSide.DisposeAsync().ConfigureAwait(false);
                }
            });
        }

        public DuplexStream Host { get; }

        public void Break()
        {
            _toDevice.Writer.TryComplete();
            _toHost.Writer.TryComplete();
            _cts.Cancel();
        }

        public async Task StopAsync()
        {
            Break();
            try
            {
                await _task.ConfigureAwait(false);
            }
            finally
            {
                await Host.DisposeAsync().ConfigureAwait(false);
                _cts.Dispose();
            }
        }
    }
}
