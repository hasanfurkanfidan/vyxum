using System.Collections.Concurrent;

namespace Vyxum.Devices.Simulator;

/// <summary>When a <see cref="LineReplaySimulator"/> sends the next recorded line.</summary>
public enum ReplayMode
{
    /// <summary>Whenever a line (any command) is received from the host.</summary>
    OnRequest,

    /// <summary>On a timer, whatever the host sends.</summary>
    Interval,
}

/// <summary>
/// Replays recorded text lines, one per request or per interval, each ended with CR LF. Every session starts at the first line.
/// With <c>loop</c> false the device goes quiet after the last line.
/// </summary>
public sealed class LineReplaySimulator : ISimulatedDevice
{
    private readonly string[] _lines;
    private readonly bool _loop;
    private readonly ConcurrentQueue<string> _received = new();

    /// <summary>Creates the simulator from lines (without terminators). At least one line is required.</summary>
    public LineReplaySimulator(IEnumerable<string> lines, ReplayMode mode = ReplayMode.OnRequest, TimeSpan? interval = null, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _lines = [.. lines];
        if (_lines.Length == 0)
        {
            throw new ArgumentException("At least one line is required.", nameof(lines));
        }

        Mode = mode;
        Interval = interval ?? TimeSpan.FromMilliseconds(50);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Interval, TimeSpan.Zero);
        _loop = loop;
    }

    /// <summary>When lines are sent.</summary>
    public ReplayMode Mode { get; }

    /// <summary>The time between lines in <see cref="ReplayMode.Interval"/>.</summary>
    public TimeSpan Interval { get; }

    /// <summary>All commands received, in order, over all sessions.</summary>
    public IReadOnlyList<string> ReceivedCommands => [.. _received];

    /// <summary>Creates the simulator from a recording file.</summary>
    public static LineReplaySimulator FromFile(string path, ReplayMode mode = ReplayMode.OnRequest, TimeSpan? interval = null, bool loop = true) =>
        new(ReadRecording(path), mode, interval, loop);

    /// <summary>
    /// Reads a recording: one line per row; rows that start with <c>#</c> and empty rows are skipped. Spaces inside and
    /// around a line are kept, because the value field of a device is padded; only CR and LF are removed.
    /// </summary>
    public static IReadOnlyList<string> ReadRecording(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return [.. File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith('#'))];
    }

    /// <inheritdoc />
    public async Task RunSessionAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = new LineReader(stream);
        var writer = new LineWriter(stream);
        var index = 0;

        async ValueTask SendNextAsync()
        {
            if (index >= _lines.Length)
            {
                if (!_loop)
                {
                    return;
                }

                index = 0;
            }

            await writer.WriteLineAsync(_lines[index++], cancellationToken).ConfigureAwait(false);
        }

        if (Mode == ReplayMode.OnRequest)
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                _received.Enqueue(line.Trim());
                await SendNextAsync().ConfigureAwait(false);
            }

            return;
        }

        var drain = Task.Run(
            async () =>
            {
                try
                {
                    while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                    {
                        if (line.Trim().Length > 0)
                        {
                            _received.Enqueue(line.Trim());
                        }
                    }
                }
                catch (Exception)
                {
                    // The connection ended.
                }
            },
            CancellationToken.None);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await SendNextAsync().ConfigureAwait(false);
        }
        while (!drain.IsCompleted && await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }
}
