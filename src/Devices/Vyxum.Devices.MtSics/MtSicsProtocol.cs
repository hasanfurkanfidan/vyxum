using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Vyxum.Devices.MtSics;

/// <summary>
/// Reads weights with MT-SICS. Polls with <c>S</c> or <c>SI</c>, or sends <c>SIR</c> once and reads the stream.
/// Apart from these read commands, only <c>Z</c> is ever sent, once per <see cref="ZeroAsync"/> call; every write goes
/// through <see cref="MtSicsCommands.EnsureSendable"/>.
/// </summary>
/// <remarks>
/// One instance serves one device and reads one connection at a time. A zero request is queued and carried out by the
/// read loop between two read commands, so commands and answers on the transport never interleave.
/// </remarks>
public sealed class MtSicsProtocol : IScaleProtocol, ISupportsZero
{
    private readonly MtSicsProtocolOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<ZeroRequest> _zeroRequests = Channel.CreateUnbounded<ZeroRequest>();
    private int _activeSessions;

    /// <summary>Creates the protocol.</summary>
    public MtSicsProtocol(MtSicsProtocolOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new MtSicsProtocolOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        MtSicsCommands.EnsureSendable(_options.Command);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<WeightReading> ReadAsync(ITransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return _options.Command == "SIR"
            ? StreamAsync(transport, cancellationToken)
            : PollAsync(transport, cancellationToken);
    }

    /// <summary>
    /// Sends <c>Z</c> once on the connection being read and maps the answer: <c>Z A</c> is done; <c>Z I</c>, <c>Z +</c>,
    /// <c>Z -</c> and <c>EL</c> are rejected; <c>ES</c> and <c>ET</c> (not executed) are resent like other commands. Without
    /// an answer within <see cref="MtSicsProtocolOptions.ZeroTimeout"/> the result is unavailable and the command is not
    /// resent, because the device may have zeroed.
    /// </summary>
    public Task<ZeroResult> ZeroAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _activeSessions) == 0)
        {
            return Task.FromResult(NotConnected());
        }

        var request = new ZeroRequest();
        _zeroRequests.Writer.TryWrite(request);

        // The session may have ended between the check and the write; then nobody would pick the request up.
        if (Volatile.Read(ref _activeSessions) == 0 && request.TryAbandon())
        {
            request.Complete(NotConnected());
        }

        return WaitAsync(request, cancellationToken);
    }

    private static async Task<ZeroResult> WaitAsync(ZeroRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await request.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Not sent yet: it never will be. Already sent: the read loop finishes the exchange on its own.
            request.TryAbandon();
            throw;
        }
    }

    private static ZeroResult NotConnected() => ZeroResult.Unavailable("The device is not connected.");

    private async IAsyncEnumerable<WeightReading> PollAsync(ITransport transport, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var session = new Session(this, transport);
        Interlocked.Increment(ref _activeSessions);
        try
        {
            while (true)
            {
                var cycleStart = _timeProvider.GetTimestamp();
                await ProcessZeroRequestsAsync(session, cancellationToken).ConfigureAwait(false);
                for (var attempt = 0; attempt <= _options.RetriesOnError; attempt++)
                {
                    await session.SendAsync(_options.Command, cancellationToken).ConfigureAwait(false);
                    var response = await session.ReadResponseAsync(_options.ResponseTimeout, cancellationToken).ConfigureAwait(false);
                    if (response is null)
                    {
                        break;
                    }

                    if (response.Kind is MtSicsResponseKind.SyntaxError or MtSicsResponseKind.TransmissionError)
                    {
                        continue;
                    }

                    var reading = ToReading(response);
                    if (reading is not null)
                    {
                        yield return reading;
                    }

                    break;
                }

                // Every cycle waits, whatever the device answered, so error answers cannot make the loop spin.
                var remaining = _options.PollInterval - _timeProvider.GetElapsedTime(cycleStart);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
        finally
        {
            EndSession();
        }
    }

    private async IAsyncEnumerable<WeightReading> StreamAsync(ITransport transport, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var session = new Session(this, transport);
        Interlocked.Increment(ref _activeSessions);
        try
        {
            await session.SendAsync(_options.Command, cancellationToken).ConfigureAwait(false);
            var retriesLeft = _options.RetriesOnError;
            while (true)
            {
                if (await ProcessZeroRequestsAsync(session, cancellationToken).ConfigureAwait(false))
                {
                    // The device may stop the stream when it receives another command: ask for it again.
                    await session.SendAsync(_options.Command, cancellationToken).ConfigureAwait(false);
                }

                var response = await session.ReadResponseAsync(_options.ResponseTimeout, cancellationToken).ConfigureAwait(false);
                if (response is null)
                {
                    // The stream may have stopped (for example after a device restart): ask for it again.
                    await session.SendAsync(_options.Command, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (response.Kind is MtSicsResponseKind.SyntaxError or MtSicsResponseKind.TransmissionError)
                {
                    if (retriesLeft > 0)
                    {
                        retriesLeft--;
                        await session.SendAsync(_options.Command, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                var reading = ToReading(response);
                if (reading is not null)
                {
                    retriesLeft = _options.RetriesOnError;
                    yield return reading;
                }
            }
        }
        finally
        {
            EndSession();
        }
    }

    /// <summary>Carries out the queued zero requests. Returns true when at least one command was sent.</summary>
    private async Task<bool> ProcessZeroRequestsAsync(Session session, CancellationToken cancellationToken)
    {
        var sent = false;
        while (_zeroRequests.Reader.TryRead(out var request))
        {
            if (!request.TryStart())
            {
                // Cancelled or abandoned before it was sent: it is never sent.
                continue;
            }

            sent = true;
            ZeroResult result;
            try
            {
                result = await session.ZeroAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                request.Complete(ZeroResult.Unavailable(
                    ex is OperationCanceledException ? "The connection to the device is closing." : ex.Message));
                throw;
            }

            request.Complete(result);
        }

        return sent;
    }

    private void EndSession()
    {
        if (Interlocked.Decrement(ref _activeSessions) > 0)
        {
            return;
        }

        while (_zeroRequests.Reader.TryRead(out var request))
        {
            if (request.TryAbandon())
            {
                request.Complete(NotConnected());
            }
        }
    }

    private WeightReading? ToReading(MtSicsResponse response)
    {
        var now = _timeProvider.GetUtcNow();
        return response.Kind switch
        {
            MtSicsResponseKind.Weight => new WeightReading(
                response.Value,
                response.Unit,
                response.IsStable ? WeightState.Stable : WeightState.Motion,
                null,
                null,
                response.Raw,
                now),
            MtSicsResponseKind.Overload => new WeightReading(null, null, WeightState.Overload, null, null, response.Raw, now),
            MtSicsResponseKind.Underload => new WeightReading(null, null, WeightState.Underload, null, null, response.Raw, now),
            _ => null,
        };
    }

    /// <summary>One queued zero request. Either the read loop starts it or the caller abandons it, never both.</summary>
    private sealed class ZeroRequest
    {
        private const int Pending = 0;
        private const int Started = 1;
        private const int Abandoned = 2;

        private readonly TaskCompletionSource<ZeroResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _state;

        public Task<ZeroResult> Task => _completion.Task;

        public bool TryStart() => Interlocked.CompareExchange(ref _state, Started, Pending) == Pending;

        public bool TryAbandon() => Interlocked.CompareExchange(ref _state, Abandoned, Pending) == Pending;

        public void Complete(ZeroResult result) => _completion.TrySetResult(result);
    }

    /// <summary>The state of one connection: frame reader and timeout count.</summary>
    private sealed class Session
    {
        private readonly MtSicsProtocol _owner;
        private readonly ITransport _transport;
        private readonly FrameReader _reader;
        private int _consecutiveTimeouts;

        public Session(MtSicsProtocol owner, ITransport transport)
        {
            _owner = owner;
            _transport = transport;
            _reader = new FrameReader(transport, new FrameReaderOptions { Terminator = owner._options.LineTerminator });

            // Drop anything left from an earlier session before the first command.
            transport.DiscardInput();
        }

        /// <summary>Writes one command. Anything but the allowed commands throws before a byte is written.</summary>
        public ValueTask SendAsync(string command, CancellationToken cancellationToken)
        {
            MtSicsCommands.EnsureSendable(command);
            return _transport.WriteAsync(Encoding.ASCII.GetBytes(command + "\r\n"), cancellationToken);
        }

        public async Task<ZeroResult> ZeroAsync(CancellationToken cancellationToken)
        {
            var options = _owner._options;
            MtSicsResponse? last = null;
            for (var attempt = 0; attempt <= options.RetriesOnError; attempt++)
            {
                await SendAsync(MtSicsCommands.Zero, cancellationToken).ConfigureAwait(false);
                var response = await ReadZeroAnswerAsync(cancellationToken).ConfigureAwait(false);
                if (response is null)
                {
                    return ZeroResult.Unavailable(
                        $"No answer to Z within {options.ZeroTimeout.TotalSeconds:0.###} s. It is not resent because the device may have zeroed.");
                }

                last = response;
                switch (response.Kind)
                {
                    case MtSicsResponseKind.SyntaxError or MtSicsResponseKind.TransmissionError:
                        // Not executed: resending is safe.
                        continue;
                    case MtSicsResponseKind.ZeroDone:
                        return ZeroResult.Done;
                    case MtSicsResponseKind.ZeroNotExecutable:
                        return ZeroResult.Rejected("Z I: the device cannot zero now, for example because the weight is in motion.");
                    case MtSicsResponseKind.ZeroUpperLimit:
                        return ZeroResult.Rejected("Z +: the weight is above the zero setting range.");
                    case MtSicsResponseKind.ZeroLowerLimit:
                        return ZeroResult.Rejected("Z -: the weight is below the zero setting range.");
                    default:
                        return ZeroResult.Rejected($"EL: the device cannot execute Z ({response.Raw}).");
                }
            }

            return ZeroResult.Rejected($"The device answered '{last?.Raw}' to Z {options.RetriesOnError + 1} times.");
        }

        /// <summary>
        /// Reads lines until the answer to <c>Z</c> (or an error line) arrives. Weight lines of a running stream and lines
        /// that are not understood are skipped. Returns null when no answer came within the zero timeout.
        /// </summary>
        private async Task<MtSicsResponse?> ReadZeroAnswerAsync(CancellationToken cancellationToken)
        {
            var timeout = _owner._options.ZeroTimeout;
            var start = _owner._timeProvider.GetTimestamp();
            while (true)
            {
                var remaining = timeout - _owner._timeProvider.GetElapsedTime(start);
                if (remaining <= TimeSpan.Zero)
                {
                    // A late answer must not be taken as the answer to the next command.
                    _reader.Reset();
                    _transport.DiscardInput();
                    return null;
                }

                var response = await ReadResponseAsync(remaining, cancellationToken).ConfigureAwait(false);
                if (response is null)
                {
                    return null;
                }

                if (response.Kind is MtSicsResponseKind.ZeroDone or MtSicsResponseKind.ZeroNotExecutable
                    or MtSicsResponseKind.ZeroUpperLimit or MtSicsResponseKind.ZeroLowerLimit
                    or MtSicsResponseKind.SyntaxError or MtSicsResponseKind.TransmissionError or MtSicsResponseKind.LogicalError)
                {
                    return response;
                }

                // A stream line (or noise) that arrived before the answer: keep reading.
            }
        }

        /// <summary>Reads one response line. Returns null on timeout; throws when the link is dead.</summary>
        public async Task<MtSicsResponse?> ReadResponseAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            string? line;
            using (var timeoutCts = new CancellationTokenSource(timeout, _owner._timeProvider))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
                    line = await _reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _consecutiveTimeouts++;
                    if (_consecutiveTimeouts >= _owner._options.MaxConsecutiveTimeouts)
                    {
                        throw new DeviceConnectionLostException(
                            $"No response from the device after {_consecutiveTimeouts} attempts ({_transport.Description}).");
                    }

                    // A late answer to this command must not be taken as the answer to the next one.
                    _reader.Reset();
                    _transport.DiscardInput();
                    return null;
                }
            }

            if (line is null)
            {
                throw new DeviceConnectionLostException($"The connection was closed by the device ({_transport.Description}).");
            }

            _consecutiveTimeouts = 0;
            return MtSicsParser.Parse(line);
        }
    }
}
