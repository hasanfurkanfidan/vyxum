using System.Runtime.CompilerServices;
using System.Text;

namespace Vyxum.Devices.MtSics;

/// <summary>
/// Reads weights with MT-SICS. Polls with <c>S</c> or <c>SI</c>, or sends <c>SIR</c> once and reads the stream.
/// Only read commands are ever sent.
/// </summary>
public sealed class MtSicsProtocol : IScaleProtocol
{
    private readonly MtSicsProtocolOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly byte[] _command;

    /// <summary>Creates the protocol.</summary>
    public MtSicsProtocol(MtSicsProtocolOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new MtSicsProtocolOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _command = Encoding.ASCII.GetBytes(_options.Command + "\r\n");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<WeightReading> ReadAsync(ITransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return _options.Command == "SIR"
            ? StreamAsync(new Session(this, transport), cancellationToken)
            : PollAsync(new Session(this, transport), cancellationToken);
    }

    private async IAsyncEnumerable<WeightReading> PollAsync(Session session, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            var cycleStart = _timeProvider.GetTimestamp();
            for (var attempt = 0; attempt <= _options.RetriesOnError; attempt++)
            {
                await session.SendCommandAsync(cancellationToken).ConfigureAwait(false);
                var response = await session.ReadResponseAsync(cancellationToken).ConfigureAwait(false);
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

    private async IAsyncEnumerable<WeightReading> StreamAsync(Session session, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await session.SendCommandAsync(cancellationToken).ConfigureAwait(false);
        var retriesLeft = _options.RetriesOnError;
        while (true)
        {
            var response = await session.ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                // The stream may have stopped (for example after a device restart): ask for it again.
                await session.SendCommandAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (response.Kind is MtSicsResponseKind.SyntaxError or MtSicsResponseKind.TransmissionError)
            {
                if (retriesLeft > 0)
                {
                    retriesLeft--;
                    await session.SendCommandAsync(cancellationToken).ConfigureAwait(false);
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

        public ValueTask SendCommandAsync(CancellationToken cancellationToken) =>
            _transport.WriteAsync(_owner._command, cancellationToken);

        /// <summary>Reads one response line. Returns null on timeout; throws when the link is dead.</summary>
        public async Task<MtSicsResponse?> ReadResponseAsync(CancellationToken cancellationToken)
        {
            string? line;
            using (var timeout = new CancellationTokenSource(_owner._options.ResponseTimeout, _owner._timeProvider))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
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
