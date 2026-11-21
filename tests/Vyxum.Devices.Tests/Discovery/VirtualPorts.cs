using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Vyxum.Devices.Discovery;
using Vyxum.Devices.Simulator;
using Vyxum.Devices.Transports;

namespace Vyxum.Devices.Tests.Discovery;

/// <summary>
/// Virtual serial ports for the scanner. Each port answers only at its own settings (through a simulated device);
/// at the same baud rate with the wrong framing it answers with garbage, and otherwise it is silent. Every write to
/// every port is recorded.
/// </summary>
internal sealed class VirtualPorts
{
    private readonly ConcurrentDictionary<string, Func<SerialTransportOptions, ITransport>> _ports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string Port, byte[] Bytes)> _writes = new();
    private readonly ConcurrentQueue<SerialTransportOptions> _opens = new();

    public IReadOnlyList<(string Port, byte[] Bytes)> Writes => [.. _writes];

    public IReadOnlyList<SerialTransportOptions> Opens => [.. _opens];

    /// <summary>A port with a device that works at exactly the given settings.</summary>
    public VirtualPorts Device(string port, ISimulatedDevice device, int baud = 9600, SerialFraming? framing = null, byte[]? garbage = null)
    {
        var right = framing ?? SerialFraming.EightNoneOne;
        _ports[port] = options =>
            options.BaudRate == baud && options.DataBits == right.DataBits && options.Parity == right.Parity && options.StopBits == right.StopBits
                ? new SimulatedTransport(device, $"{port} sim")
                : new NoiseTransport(options.BaudRate == baud ? garbage ?? Encoding.Latin1.GetBytes("\x3F\x3FS \x3FS        572 g\r\n") : null);
        return this;
    }

    /// <summary>A port where nothing ever answers.</summary>
    public VirtualPorts Silent(string port)
    {
        _ports[port] = _ => new NoiseTransport(null);
        return this;
    }

    /// <summary>A port held by another program: opening it fails like a real serial port in use.</summary>
    public VirtualPorts HeldByOtherProgram(string port)
    {
        _ports[port] = _ => new NoiseTransport(null) { OpenException = new UnauthorizedAccessException($"Access to the port '{port}' is denied.") };
        return this;
    }

    /// <summary>A port whose open never finishes in time.</summary>
    public VirtualPorts Hanging(string port)
    {
        _ports[port] = _ => new NoiseTransport(null) { OpenDelay = TimeSpan.FromSeconds(3) };
        return this;
    }

    /// <summary>A port that opens but whose writes never finish and ignore cancellation, like a Bluetooth port without a device.</summary>
    public VirtualPorts HangingWrite(string port)
    {
        _ports[port] = _ => new NoiseTransport(null) { HangWrites = true };
        return this;
    }

    /// <summary>A port that opens but whose writes time out, like a Bluetooth port without a device.</summary>
    public VirtualPorts FailingWrite(string port)
    {
        _ports[port] = _ => new NoiseTransport(null) { FailWrites = true };
        return this;
    }

    public PortScannerOptions Options(Action<PortScannerOptions>? configure = null)
    {
        var options = new PortScannerOptions
        {
            ListenTime = TimeSpan.FromMilliseconds(120),
            ResponseTimeout = TimeSpan.FromMilliseconds(120),
            OpenTimeout = TimeSpan.FromSeconds(1),
            GetPortNames = () => [.. _ports.Keys.Order(StringComparer.OrdinalIgnoreCase)],
            CreateSerialTransport = options =>
            {
                _opens.Enqueue(options);
                return new RecordingTransport(_ports[options.PortName](options), bytes => _writes.Enqueue((options.PortName, bytes)));
            },
        };
        configure?.Invoke(options);
        return options;
    }

    /// <summary>Records every write of an inner transport.</summary>
    internal sealed class RecordingTransport(ITransport inner, Action<byte[]> record) : ITransport
    {
        public string Description => inner.Description;

        public Task OpenAsync(CancellationToken cancellationToken) => inner.OpenAsync(cancellationToken);

        public Task CloseAsync() => inner.CloseAsync();

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            record(buffer.ToArray());
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) => inner.ReadAsync(buffer, cancellationToken);

        public void DiscardInput() => inner.DiscardInput();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    /// <summary>A link at the wrong settings: silent, or answering every write with garbage.</summary>
    internal sealed class NoiseTransport(byte[]? answer) : ITransport
    {
        private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
        private byte[] _current = [];
        private int _position;

        public Exception? OpenException { get; init; }

        public TimeSpan OpenDelay { get; init; }

        public bool HangWrites { get; init; }

        public bool FailWrites { get; init; }

        public string Description => "noise";

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            if (OpenDelay > TimeSpan.Zero)
            {
                Thread.Sleep(OpenDelay);
            }

            return OpenException is null ? Task.CompletedTask : Task.FromException(OpenException);
        }

        public Task CloseAsync() => Task.CompletedTask;

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (FailWrites)
            {
                return ValueTask.FromException(new IOException("The serial port failed while writing: The write timed out."));
            }

            if (HangWrites)
            {
                return new ValueTask(new TaskCompletionSource().Task);
            }

            if (answer is not null)
            {
                _incoming.Writer.TryWrite(answer);
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_position >= _current.Length)
            {
                _current = await _incoming.Reader.ReadAsync(cancellationToken);
                _position = 0;
            }

            var count = Math.Min(buffer.Length, _current.Length - _position);
            _current.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public void DiscardInput()
        {
            while (_incoming.Reader.TryRead(out _))
            {
            }

            _current = [];
            _position = 0;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
