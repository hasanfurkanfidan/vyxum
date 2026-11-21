using System.Runtime.CompilerServices;
using System.Text;

namespace Vyxum.Devices.GenericAscii;

/// <summary>
/// Reads weights from any line-based ASCII output, described by a <see cref="GenericAsciiOptions"/>.
/// Listen-only by default; in demand mode it sends <see cref="GenericAsciiOptions.RequestCommand"/> before each read.
/// </summary>
public sealed class GenericAsciiProtocol : IScaleProtocol
{
    private readonly GenericAsciiOptions _options;
    private readonly GenericAsciiParser _parser;
    private readonly TimeProvider _timeProvider;
    private readonly byte[]? _request;

    /// <summary>Creates the protocol. Throws <see cref="ArgumentException"/> on invalid settings.</summary>
    public GenericAsciiProtocol(GenericAsciiOptions options, TimeProvider? timeProvider = null)
    {
        _parser = new GenericAsciiParser(options);
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _request = options.IsDemandMode ? Encoding.Latin1.GetBytes(options.RequestCommand + options.RequestTerminator) : null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<WeightReading> ReadAsync(ITransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var reader = new FrameReader(transport, new FrameReaderOptions
        {
            Terminator = _options.Terminator,
            MaxFrameLength = _options.MaxLineLength,
        });
        return _request is null
            ? ListenAsync(transport, reader, cancellationToken)
            : PollAsync(transport, reader, _request, cancellationToken);
    }

    private async IAsyncEnumerable<WeightReading> ListenAsync(
        ITransport transport,
        FrameReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        transport.DiscardInput();
        var lastReading = _timeProvider.GetTimestamp();
        var linesSinceReading = 0;
        while (true)
        {
            var remaining = _options.InactivityTimeout - _timeProvider.GetElapsedTime(lastReading);
            var line = remaining > TimeSpan.Zero
                ? await ReadLineAsync(transport, reader, remaining, cancellationToken).ConfigureAwait(false)
                : null;
            if (line is null)
            {
                var detail = linesSinceReading > 0
                    ? $"{linesSinceReading} lines were received but none matched the pattern"
                    : "nothing was received";
                throw new DeviceConnectionLostException(
                    $"No reading from the device for {_options.InactivityTimeout.TotalSeconds:0.###} s; {detail} ({transport.Description}).");
            }

            var reading = _parser.Parse(line, _timeProvider.GetUtcNow());
            if (reading is null)
            {
                linesSinceReading++;
                continue;
            }

            linesSinceReading = 0;
            lastReading = _timeProvider.GetTimestamp();
            yield return reading;
        }
    }

    private async IAsyncEnumerable<WeightReading> PollAsync(
        ITransport transport,
        FrameReader reader,
        byte[] request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        transport.DiscardInput();
        var timeouts = 0;
        while (true)
        {
            var cycleStart = _timeProvider.GetTimestamp();
            await transport.WriteAsync(request, cancellationToken).ConfigureAwait(false);

            // Lines that do not match (echo, headers) are skipped until a matching one arrives or time runs out.
            WeightReading? reading = null;
            while (reading is null)
            {
                var remaining = _options.ResponseTimeout - _timeProvider.GetElapsedTime(cycleStart);
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var line = await ReadLineAsync(transport, reader, remaining, cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                reading = _parser.Parse(line, _timeProvider.GetUtcNow());
            }

            if (reading is null)
            {
                timeouts++;
                if (timeouts >= _options.MaxConsecutiveTimeouts)
                {
                    throw new DeviceConnectionLostException(
                        $"No matching response from the device after {timeouts} requests ({transport.Description}).");
                }

                // A late answer to this request must not be taken as the answer to the next one.
                reader.Reset();
                transport.DiscardInput();
            }
            else
            {
                timeouts = 0;
                yield return reading;
            }

            var wait = _options.PollInterval - _timeProvider.GetElapsedTime(cycleStart);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    /// <summary>Reads one line. Returns null on timeout; throws when the remote side closed the connection.</summary>
    private async Task<string?> ReadLineAsync(ITransport transport, FrameReader reader, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        string? line;
        try
        {
            line = await reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return line ?? throw new DeviceConnectionLostException($"The connection was closed by the device ({transport.Description}).");
    }
}
