namespace Vyxum.Devices.ToledoContinuous;

/// <summary>
/// Recognises Toledo Continuous output in bytes a device sent on its own: at least two valid frames (STX, three status
/// words, weight and tare, CR). Detects whether a checksum byte follows CR. Listen-only: nothing is written.
/// </summary>
public sealed class ToledoContinuousProbe : IPassiveProbe
{
    private const byte Stx = 0x02;
    private const byte Cr = 0x0D;
    private const int FrameLength = ToledoContinuousParser.PayloadLength + 2;
    private const int MinimumFrames = 2;

    /// <inheritdoc />
    public ProbeMatch? Recognise(ReadOnlySpan<byte> received, DateTimeOffset timestamp)
    {
        var plain = 0;
        var withChecksum = 0;
        ToledoContinuousFrame? lastPlain = null;
        ToledoContinuousFrame? lastWithChecksum = null;
        for (var i = 0; i + FrameLength <= received.Length; i++)
        {
            if (received[i] != Stx || received[i + FrameLength - 1] != Cr)
            {
                continue;
            }

            var frame = ToledoContinuousParser.Parse(received.Slice(i, FrameLength));
            if (!frame.IsValid)
            {
                continue;
            }

            plain++;
            lastPlain = frame;
            if (i + FrameLength < received.Length)
            {
                var checkedFrame = ToledoContinuousParser.Parse(received.Slice(i, FrameLength + 1), checksumEnabled: true);
                if (checkedFrame.IsValid)
                {
                    withChecksum++;
                    lastWithChecksum = checkedFrame;
                }
            }
        }

        if (plain < MinimumFrames)
        {
            return null;
        }

        // A checksum byte matches by chance once in 128 frames, so all but possibly the last frame must carry one.
        var checksum = withChecksum >= MinimumFrames && withChecksum >= plain - 1;
        var sample = checksum ? lastWithChecksum! : lastPlain!;
        var settings = new Dictionary<string, object>(StringComparer.Ordinal) { ["checksumEnabled"] = checksum };
        return new ProbeMatch("toledo-continuous", "Toledo Continuous", settings, sample.ToReading(timestamp));
    }
}
