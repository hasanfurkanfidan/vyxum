using System.Text;

namespace Vyxum.Devices.MtSics;

/// <summary>
/// Finds an MT-SICS device by sending <c>SI</c> (read the weight once) and, when it answers with a weight, <c>I4</c> for
/// the serial number. Nothing else is ever written (decisions V-28 and V-40): every write goes through
/// <see cref="MtSicsCommands.EnsureDiscoverySendable"/>.
/// </summary>
/// <remarks>
/// <c>SI</c> is sent a second time when the first answer is <c>ES</c>, <c>ET</c>, <c>S I</c> (busy) or a line that is not
/// understood, and, when <see cref="ProbeContext.ResendOnSilence"/> is set, after a timeout. Some indicators answer the
/// first command after the port opens with <c>ES</c> (seen on the IND231).
/// </remarks>
public sealed class MtSicsProbe : IActiveProbe
{
    private const string Terminator = "\r\n";
    private const string ReadCommand = "SI";

    /// <inheritdoc />
    public IReadOnlyList<byte[]> AllowedWrites { get; } =
        [.. MtSicsCommands.Discovery.Select(c => Encoding.ASCII.GetBytes(c + Terminator))];

    /// <inheritdoc />
    public async Task<ProbeMatch?> ProbeAsync(ITransport transport, ProbeContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(context);

        var lines = new LineCollector(transport, context.TimeProvider);
        MtSicsResponse? answer = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            lines.Clear();
            transport.DiscardInput();
            await SendAsync(transport, ReadCommand, cancellationToken).ConfigureAwait(false);
            var (line, anyBytes) = await lines.ReadLineAsync(context.ResponseTimeout, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                if (!anyBytes && !context.ResendOnSilence)
                {
                    return null;
                }

                continue;
            }

            var response = MtSicsParser.Parse(line);
            if (response.Kind is MtSicsResponseKind.Weight or MtSicsResponseKind.Overload or MtSicsResponseKind.Underload)
            {
                answer = response;
                break;
            }

            if (response.Kind == MtSicsResponseKind.Busy)
            {
                // A busy answer is MT-SICS too; keep it in case the second attempt is busy as well.
                answer = response;
            }
        }

        if (answer is null)
        {
            return null;
        }

        var serialNumber = await ReadSerialNumberAsync(transport, lines, context, cancellationToken).ConfigureAwait(false);
        var settings = new Dictionary<string, object>(StringComparer.Ordinal) { ["command"] = ReadCommand };
        return new ProbeMatch("mt-sics", "MT-SICS", settings, ToReading(answer, context.TimeProvider.GetUtcNow()), serialNumber);
    }

    private static async Task<string?> ReadSerialNumberAsync(ITransport transport, LineCollector lines, ProbeContext context, CancellationToken cancellationToken)
    {
        lines.Clear();
        transport.DiscardInput();
        await SendAsync(transport, MtSicsCommands.SerialNumber, cancellationToken).ConfigureAwait(false);
        var start = context.TimeProvider.GetTimestamp();
        while (true)
        {
            var remaining = context.ResponseTimeout - context.TimeProvider.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var (line, _) = await lines.ReadLineAsync(remaining, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            var response = MtSicsParser.Parse(line);
            if (response.Kind == MtSicsResponseKind.SerialNumber)
            {
                return response.SerialNumber;
            }

            if (response.Kind is MtSicsResponseKind.SyntaxError or MtSicsResponseKind.TransmissionError or MtSicsResponseKind.LogicalError)
            {
                // The device does not support I4; the serial number stays unknown.
                return null;
            }
        }
    }

    private static ValueTask SendAsync(ITransport transport, string command, CancellationToken cancellationToken)
    {
        MtSicsCommands.EnsureDiscoverySendable(command);
        return transport.WriteAsync(Encoding.ASCII.GetBytes(command + Terminator), cancellationToken);
    }

    private static WeightReading? ToReading(MtSicsResponse response, DateTimeOffset now) => response.Kind switch
    {
        MtSicsResponseKind.Weight => new WeightReading(
            response.Value, response.Unit, response.IsStable ? WeightState.Stable : WeightState.Motion, null, null, response.Raw, now),
        MtSicsResponseKind.Overload => new WeightReading(null, null, WeightState.Overload, null, null, response.Raw, now),
        MtSicsResponseKind.Underload => new WeightReading(null, null, WeightState.Underload, null, null, response.Raw, now),
        _ => null,
    };

    /// <summary>Collects bytes into CR LF (or LF) terminated lines and remembers whether any byte arrived.</summary>
    private sealed class LineCollector(ITransport transport, TimeProvider time)
    {
        private const int MaxLineLength = 256;
        private readonly byte[] _buffer = new byte[256];
        private readonly List<byte> _pending = [];

        public void Clear() => _pending.Clear();

        /// <summary>Returns the next line (without terminator), or null on timeout, and whether any byte arrived during the call.</summary>
        public async Task<(string? Line, bool AnyBytes)> ReadLineAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var anyBytes = false;
            var start = time.GetTimestamp();
            while (true)
            {
                var newline = _pending.IndexOf((byte)'\n');
                if (newline >= 0)
                {
                    var length = newline > 0 && _pending[newline - 1] == (byte)'\r' ? newline - 1 : newline;
                    var line = Encoding.Latin1.GetString([.. _pending.GetRange(0, length)]);
                    _pending.RemoveRange(0, newline + 1);
                    return (line, true);
                }

                if (_pending.Count > MaxLineLength)
                {
                    _pending.Clear();
                }

                var remaining = timeout - time.GetElapsedTime(start);
                if (remaining <= TimeSpan.Zero)
                {
                    return (null, anyBytes);
                }

                using var timeoutCts = new CancellationTokenSource(remaining, time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                int count;
                try
                {
                    count = await transport.ReadAsync(_buffer, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return (null, anyBytes);
                }

                if (count == 0)
                {
                    throw new DeviceConnectionLostException($"The connection was closed by the device ({transport.Description}).");
                }

                anyBytes = true;
                _pending.AddRange(_buffer.AsSpan(0, count));
            }
        }
    }
}
