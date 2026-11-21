using System.Threading.Channels;

namespace Vyxum.Devices;

/// <summary>
/// Keeps one device connected: opens the transport, runs the protocol, forwards readings and, after any
/// failure, closes the transport and reopens it with an increasing wait.
/// </summary>
public sealed class DeviceRunner
{
    private readonly Func<ITransport> _transportFactory;
    private readonly IScaleProtocol _protocol;
    private readonly DeviceRunnerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<WeightReading> _readings;
    private readonly Lock _statusLock = new();
    private DeviceStatusInfo _status;
    private int _started;

    /// <summary>Creates a runner. Call <see cref="RunAsync"/> to start it.</summary>
    public DeviceRunner(
        string deviceId,
        Func<ITransport> transportFactory,
        IScaleProtocol protocol,
        DeviceRunnerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(protocol);
        DeviceId = deviceId;
        _transportFactory = transportFactory;
        _protocol = protocol;
        _options = options ?? new DeviceRunnerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _readings = Channel.CreateBounded<WeightReading>(new BoundedChannelOptions(_options.ReadingBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
        });
        _status = new DeviceStatusInfo(deviceId, DeviceStatus.Disconnected, null, null, _timeProvider.GetUtcNow());
    }

    /// <summary>The device this runner serves.</summary>
    public string DeviceId { get; }

    /// <summary>The current status.</summary>
    public DeviceStatusInfo Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
    }

    /// <summary>Readings in arrival order. Completes when <see cref="RunAsync"/> ends.</summary>
    public ChannelReader<WeightReading> Readings => _readings.Reader;

    /// <summary>Raised on every status change, on the runner's thread. Handlers should return quickly and not throw.</summary>
    public event EventHandler<DeviceStatusInfo>? StatusChanged;

    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled, then returns without throwing. Can be called once.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The runner has already been started.");
        }

        var failures = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SetStatus(DeviceStatus.Connecting, null, null);
                string error;
                ITransport? transport = null;
                try
                {
                    transport = _transportFactory();
                    await transport.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await foreach (var reading in _protocol.ReadAsync(transport, cancellationToken).ConfigureAwait(false))
                    {
                        failures = 0;
                        if (Status.Status != DeviceStatus.Connected)
                        {
                            SetStatus(DeviceStatus.Connected, null, null);
                        }

                        _readings.Writer.TryWrite(reading);
                    }

                    error = "The protocol stopped without an error.";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                finally
                {
                    await CloseQuietlyAsync(transport).ConfigureAwait(false);
                }

                failures++;
                var delay = _options.GetReconnectDelay(failures);
                SetStatus(DeviceStatus.Disconnected, error, delay);
                try
                {
                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            SetStatus(DeviceStatus.Disconnected, null, null);
            _readings.Writer.TryComplete();
        }
    }

    private static async Task CloseQuietlyAsync(ITransport? transport)
    {
        if (transport is null)
        {
            return;
        }

        try
        {
            await transport.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The connection is being dropped anyway; a failing close must not stop the runner.
        }

        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Same as above.
        }
    }

    private void SetStatus(DeviceStatus status, string? error, TimeSpan? retryIn)
    {
        var info = new DeviceStatusInfo(DeviceId, status, error, retryIn, _timeProvider.GetUtcNow());
        lock (_statusLock)
        {
            if (_status.Status == status && _status.LastError == error && _status.RetryIn == retryIn)
            {
                return;
            }

            _status = info;
        }

        StatusChanged?.Invoke(this, info);
    }
}
