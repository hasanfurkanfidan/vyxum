using System.Net.Sockets;
using Vyxum.Devices.Transports;

namespace Vyxum.Devices.Discovery;

/// <summary>The state of one port during and after a scan.</summary>
public enum PortScanState
{
    /// <summary>Waiting to be scanned.</summary>
    Pending,

    /// <summary>Being scanned.</summary>
    Scanning,

    /// <summary>A device was found.</summary>
    Found,

    /// <summary>No device answered with any of the tried settings.</summary>
    NotFound,

    /// <summary>The port is used by a running device or another program; it was not touched.</summary>
    InUse,

    /// <summary>The port could not be opened or failed.</summary>
    Error,
}

/// <summary>The state of one port.</summary>
/// <param name="PortName">The serial port name (for example <c>COM3</c>) or <c>host:port</c> for the network probe.</param>
/// <param name="State">The state.</param>
/// <param name="Detail">A short English explanation for errors and ports in use; null otherwise.</param>
public sealed record PortScanStatus(string PortName, PortScanState State, string? Detail = null);

/// <summary>A device found by a scan.</summary>
/// <param name="PortName">The serial port; null for the network probe.</param>
/// <param name="Serial">The serial settings it answered with; null for the network probe.</param>
/// <param name="Tcp">The network address; null for a serial port.</param>
/// <param name="Match">The protocol, its settings and a sample reading.</param>
/// <param name="Description">A short description, for example <c>MT-SICS, 9600 8N1</c>.</param>
public sealed record DiscoveredDevice(
    string? PortName,
    SerialTransportOptions? Serial,
    TcpTransportOptions? Tcp,
    ProbeMatch Match,
    string Description);

/// <summary>What to scan.</summary>
/// <param name="PortsInUse">Serial ports that must not be opened (used by running devices).</param>
/// <param name="Tcp">When set, only this network address is probed and no serial port is scanned.</param>
public sealed record PortScanRequest(IReadOnlyCollection<string> PortsInUse, TcpTransportOptions? Tcp = null);

/// <summary>The result of a scan.</summary>
/// <param name="Ports">Every port with its final state, in port order.</param>
/// <param name="Devices">The devices found, in port order.</param>
public sealed record PortScanReport(IReadOnlyList<PortScanStatus> Ports, IReadOnlyList<DiscoveredDevice> Devices);

/// <summary>
/// Finds scales on the serial ports of this computer, or at one network address, without changing anything on them
/// (decision V-40). The engine knows no protocol: it opens a port with each candidate setting, collects what the device
/// sends on its own and hands it to the passive probes, then lets the active probes ask with their read-only commands.
/// </summary>
/// <remarks>
/// <para>Per serial port, baud rates are tried in order and, at each baud rate, the framings in order; the first match
/// ends the port. The port is closed after every attempt. Ports in use are reported and never opened; ports are scanned
/// in parallel up to <see cref="PortScannerOptions.MaxParallelPorts"/>.</para>
/// <para>To keep a scan short, the framings after the first are shortened: they listen only when the first framing at
/// that baud rate received bytes while listening (a device that sends on its own is heard at the right baud rate whatever
/// the framing), and active probes resend after silence only with the first framing.</para>
/// <para>Every write goes through a <see cref="WriteGuardTransport"/> that allows only the byte sequences the active
/// probes declare in <see cref="IActiveProbe.AllowedWrites"/>.</para>
/// </remarks>
public sealed class PortScanner
{
    private const int MaxListenBytes = 4096;

    private readonly PortScannerOptions _options;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<byte[]> _allowedWrites;

    /// <summary>Creates the scanner.</summary>
    public PortScanner(PortScannerOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new PortScannerOptions();
        _time = timeProvider ?? TimeProvider.System;
        _allowedWrites = [.. _options.ActiveProbes.SelectMany(p => p.AllowedWrites)];
    }

    /// <summary>Every byte sequence the scanner may write: the union of the active probes' allowed writes.</summary>
    public IReadOnlyList<byte[]> AllowedWrites => _allowedWrites;

    /// <summary>
    /// Scans and returns every port with its state and the devices found. <paramref name="progress"/> gets a snapshot of
    /// all port states whenever one changes. Throws <see cref="OperationCanceledException"/> when cancelled; ports are
    /// closed first.
    /// </summary>
    public async Task<PortScanReport> ScanAsync(
        PortScanRequest request,
        IProgress<IReadOnlyList<PortScanStatus>>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var board = new Board(progress);
        if (request.Tcp is { } tcp)
        {
            var name = $"{tcp.Host}:{tcp.Port}";
            board.Add(name, PortScanState.Pending);
            board.Publish();
            await ScanTcpAsync(board, name, tcp, cancellationToken).ConfigureAwait(false);
            return board.ToReport();
        }

        var inUse = new HashSet<string>(request.PortsInUse ?? [], StringComparer.OrdinalIgnoreCase);
        var ports = _options.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var port in ports)
        {
            if (inUse.Contains(port))
            {
                board.Add(port, PortScanState.InUse, "The port is used by a device of this agent.");
            }
            else
            {
                board.Add(port, PortScanState.Pending);
            }
        }

        board.Publish();
        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxParallelPorts));
        var tasks = ports.Where(p => !inUse.Contains(p)).Select(async port =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ScanSerialAsync(board, port, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return board.ToReport();
    }

    private async Task ScanSerialAsync(Board board, string port, CancellationToken cancellationToken)
    {
        board.Set(port, PortScanState.Scanning);
        using var portCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        portCts.CancelAfter(_options.PortTimeout);
        try
        {
            foreach (var baud in _options.BaudRates)
            {
                var heardAtPrimary = false;
                for (var f = 0; f < _options.Framings.Count; f++)
                {
                    var framing = _options.Framings[f];
                    var primary = f == 0;
                    if (!primary && _options.ActiveProbes.Count == 0 && !heardAtPrimary)
                    {
                        continue;
                    }

                    var serial = new SerialTransportOptions
                    {
                        PortName = port,
                        BaudRate = baud,
                        DataBits = framing.DataBits,
                        Parity = framing.Parity,
                        StopBits = framing.StopBits,
                    };
                    var attempt = await TryAsync(
                        () => _options.CreateSerialTransport(serial),
                        listen: primary || heardAtPrimary,
                        resendOnSilence: primary,
                        portCts.Token).ConfigureAwait(false);
                    switch (attempt.Kind)
                    {
                        case AttemptKind.Match:
                            board.Found(port, new DiscoveredDevice(port, serial, null, attempt.Match!, $"{attempt.Match!.ProtocolName}, {baud} {framing}"));
                            return;
                        case AttemptKind.InUse:
                            board.Set(port, PortScanState.InUse, attempt.Detail);
                            return;
                        case AttemptKind.OpenFailed:
                            board.Set(port, PortScanState.Error, attempt.Detail);
                            return;
                    }

                    if (primary)
                    {
                        heardAtPrimary = attempt.HeardWhileListening;
                    }
                }
            }

            board.Set(port, PortScanState.NotFound);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            board.Set(port, PortScanState.NotFound, $"The scan of the port was stopped after {_options.PortTimeout.TotalSeconds:0} s.");
        }
    }

    private async Task ScanTcpAsync(Board board, string name, TcpTransportOptions tcp, CancellationToken cancellationToken)
    {
        board.Set(name, PortScanState.Scanning);
        var options = tcp with { ConnectTimeout = _options.TcpConnectTimeout };
        var attempt = await TryAsync(() => _options.CreateTcpTransport(options), listen: true, resendOnSilence: true, cancellationToken)
            .ConfigureAwait(false);
        switch (attempt.Kind)
        {
            case AttemptKind.Match:
                board.Found(name, new DiscoveredDevice(null, null, tcp, attempt.Match!, $"{attempt.Match!.ProtocolName}, {name}"));
                break;
            case AttemptKind.InUse or AttemptKind.OpenFailed:
                board.Set(name, PortScanState.Error, attempt.Detail);
                break;
            default:
                board.Set(name, PortScanState.NotFound);
                break;
        }
    }

    /// <summary>One attempt: open, listen, probe, close.</summary>
    private async Task<Attempt> TryAsync(Func<ITransport> create, bool listen, bool resendOnSilence, CancellationToken cancellationToken)
    {
        var transport = new WriteGuardTransport(create(), _allowedWrites);
        var open = Task.Run(() => transport.OpenAsync(cancellationToken), CancellationToken.None);
        try
        {
            await open.WaitAsync(_options.OpenTimeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (ex is TimeoutException)
            {
                // The open may still finish later; close the port then.
                _ = open.ContinueWith(_ => transport.DisposeAsync().AsTask(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                return Attempt.Failed(AttemptKind.OpenFailed, $"The port did not open within {_options.OpenTimeout.TotalSeconds:0.#} s.");
            }

            await transport.DisposeAsync().ConfigureAwait(false);
            return ex switch
            {
                UnauthorizedAccessException => Attempt.Failed(AttemptKind.InUse, "The port is used by another program."),
                IOException or SocketException or InvalidOperationException => Attempt.Failed(AttemptKind.OpenFailed, ex.Message),
                _ => throw new InvalidOperationException("Opening the port failed unexpectedly: " + ex.Message, ex),
            };
        }
        catch (OperationCanceledException)
        {
            _ = open.ContinueWith(_ => transport.DisposeAsync().AsTask(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            throw;
        }

        try
        {
            var heard = false;
            if (listen)
            {
                var received = await ListenAsync(transport, cancellationToken).ConfigureAwait(false);
                heard = received.Length > 0;
                var now = _time.GetUtcNow();
                foreach (var probe in _options.PassiveProbes)
                {
                    if (probe.Recognise(received, now) is { } passive)
                    {
                        return Attempt.Matched(passive, heard);
                    }
                }
            }

            var context = new ProbeContext(_options.ResponseTimeout, resendOnSilence, _time);
            foreach (var probe in _options.ActiveProbes)
            {
                transport.DiscardInput();
                if (await probe.ProbeAsync(transport, context, cancellationToken).ConfigureAwait(false) is { } active)
                {
                    return Attempt.Matched(active, heard);
                }
            }

            return new Attempt(AttemptKind.NoMatch, null, null, heard);
        }
        catch (Exception ex) when (ex is IOException or DeviceConnectionLostException or TimeoutException)
        {
            // The link failed during this attempt (wrong settings can look like that); try the next setting.
            return new Attempt(AttemptKind.NoMatch, null, ex.Message, transport.BytesRead > 0);
        }
        finally
        {
            await transport.CloseAsync().ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<byte[]> ListenAsync(ITransport transport, CancellationToken cancellationToken)
    {
        transport.DiscardInput();
        var received = new List<byte>();
        var buffer = new byte[512];
        var start = _time.GetTimestamp();
        while (received.Count < MaxListenBytes)
        {
            var remaining = _options.ListenTime - _time.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            using var timeout = new CancellationTokenSource(remaining, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            int count;
            try
            {
                count = await transport.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (count == 0)
            {
                break;
            }

            received.AddRange(buffer.AsSpan(0, count));
        }

        return [.. received];
    }

    private enum AttemptKind
    {
        NoMatch,
        Match,
        InUse,
        OpenFailed,
    }

    private sealed record Attempt(AttemptKind Kind, ProbeMatch? Match, string? Detail, bool HeardWhileListening)
    {
        public static Attempt Matched(ProbeMatch match, bool heard) => new(AttemptKind.Match, match, null, heard);

        public static Attempt Failed(AttemptKind kind, string detail) => new(kind, null, detail, false);
    }

    /// <summary>The port states and devices found, shared by the parallel port scans.</summary>
    private sealed class Board(IProgress<IReadOnlyList<PortScanStatus>>? progress)
    {
        private readonly Lock _lock = new();
        private readonly List<PortScanStatus> _ports = [];
        private readonly Dictionary<string, DiscoveredDevice> _devices = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string port, PortScanState state, string? detail = null)
        {
            lock (_lock)
            {
                _ports.Add(new PortScanStatus(port, state, detail));
            }
        }

        public void Set(string port, PortScanState state, string? detail = null)
        {
            lock (_lock)
            {
                var index = _ports.FindIndex(p => string.Equals(p.PortName, port, StringComparison.OrdinalIgnoreCase));
                _ports[index] = new PortScanStatus(_ports[index].PortName, state, detail);
            }

            Publish();
        }

        public void Found(string port, DiscoveredDevice device)
        {
            lock (_lock)
            {
                _devices[port] = device;
            }

            Set(port, PortScanState.Found);
        }

        public void Publish()
        {
            if (progress is null)
            {
                return;
            }

            IReadOnlyList<PortScanStatus> snapshot;
            lock (_lock)
            {
                snapshot = [.. _ports];
            }

            progress.Report(snapshot);
        }

        public PortScanReport ToReport()
        {
            lock (_lock)
            {
                var ports = _ports.ToList();
                var devices = ports.Select(p => _devices.GetValueOrDefault(p.PortName)).OfType<DiscoveredDevice>().ToList();
                return new PortScanReport(ports, devices);
            }
        }
    }
}
