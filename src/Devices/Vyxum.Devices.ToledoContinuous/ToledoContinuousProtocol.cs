using System.Runtime.CompilerServices;

namespace Vyxum.Devices.ToledoContinuous;

/// <summary>
/// Reads the Toledo Continuous output that an indicator sends on its own. Listen-only: nothing is ever
/// written to the transport.
/// </summary>
public sealed class ToledoContinuousProtocol : IScaleProtocol
{
    private const int MaxFrameLength = 32;

    private readonly ToledoContinuousOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the protocol.</summary>
    public ToledoContinuousProtocol(ToledoContinuousOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new ToledoContinuousOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<WeightReading> ReadAsync(ITransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return ReadCoreAsync(transport, cancellationToken);
    }

    private async IAsyncEnumerable<WeightReading> ReadCoreAsync(
        ITransport transport,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = new FrameReader(transport, new FrameReaderOptions
        {
            Mode = FrameMode.StxCr,
            TrailingByteCount = _options.ChecksumEnabled ? 1 : 0,
            MaxFrameLength = MaxFrameLength,
        });

        // Drop anything left from an earlier session; the next frame start is found by STX.
        transport.DiscardInput();

        var lastValid = _timeProvider.GetTimestamp();
        var invalidInARow = 0;
        string? lastInvalidReason = null;
        while (true)
        {
            var remaining = _options.InactivityTimeout - _timeProvider.GetElapsedTime(lastValid);
            Frame? frame = null;
            var timedOut = remaining <= TimeSpan.Zero;
            if (!timedOut)
            {
                using var timeout = new CancellationTokenSource(remaining, _timeProvider);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                try
                {
                    frame = await reader.ReadFrameAsync(linked.Token).ConfigureAwait(false);
                    if (frame is null)
                    {
                        throw new DeviceConnectionLostException($"The connection was closed by the device ({transport.Description}).");
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    timedOut = true;
                }
            }

            if (timedOut)
            {
                var detail = lastInvalidReason is null ? string.Empty : $" Last invalid frame: {lastInvalidReason}";
                throw new DeviceConnectionLostException(
                    $"No valid frame from the device for {_options.InactivityTimeout.TotalSeconds:0.###} s ({transport.Description}).{detail}");
            }

            var parsed = ToledoContinuousParser.Parse(frame!, _options.ChecksumEnabled);
            if (!parsed.IsValid)
            {
                invalidInARow++;
                lastInvalidReason = parsed.InvalidReason;
                if (invalidInARow >= _options.MaxConsecutiveInvalidFrames)
                {
                    throw new DeviceConnectionLostException(
                        $"{invalidInARow} invalid frames in a row; check baud rate, parity and checksum settings ({transport.Description}). " +
                        $"Last: {parsed.InvalidReason}");
                }

                continue;
            }

            invalidInARow = 0;
            lastInvalidReason = null;
            lastValid = _timeProvider.GetTimestamp();
            yield return parsed.ToReading(_timeProvider.GetUtcNow());
        }
    }
}
