using System.Text;
using Shouldly;
using Vyxum.Devices.GenericAscii;
using Vyxum.Devices.Simulator;
using Vyxum.Devices.ToledoContinuous;
using Xunit;

namespace Vyxum.Devices.Tests.Discovery;

public class ProbeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static byte[] ToledoStream(int frames, bool checksum, int skipFirstBytes = 0)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < frames; i++)
        {
            bytes.AddRange(ToledoContinuousSimulator.BuildFrame(new SimulatedWeight(572m, "g"), checksum));
        }

        return [.. bytes.Skip(skipFirstBytes)];
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 7)]
    [InlineData(true, 5)]
    public void Toledo_frames_are_recognised_with_or_without_checksum(bool checksum, int skip)
    {
        var match = new ToledoContinuousProbe().Recognise(ToledoStream(4, checksum, skip), Now);

        match.ShouldNotBeNull();
        match.ProtocolType.ShouldBe("toledo-continuous");
        match.ProtocolSettings["checksumEnabled"].ShouldBe(checksum);
        match.Reading!.Value.ShouldBe(572m);
        match.Reading.Unit.ShouldBe("g");
    }

    [Fact]
    public void A_single_toledo_frame_is_not_enough()
    {
        new ToledoContinuousProbe().Recognise(ToledoStream(1, false), Now).ShouldBeNull();
    }

    [Fact]
    public void Toledo_frames_read_with_the_wrong_framing_are_not_recognised()
    {
        // 7E1 bytes read as 8N1 have bit 7 set where the parity bit was 1.
        var garbled = ToledoStream(4, false).Select(b => (byte)(b | 0x80)).ToArray();

        new ToledoContinuousProbe().Recognise(garbled, Now).ShouldBeNull();
        new GenericAsciiProbe().Recognise(garbled, Now).ShouldBeNull();
    }

    [Theory]
    [InlineData("0.255 kg\r\n+0001.250 kg\r\n+0001.250 kg\r\n", "1.250", "kg", ".")]
    [InlineData("    572 g\r\n    572 g\r\n", "572", "g", ".")]
    [InlineData("ST,GS,  12,5 kg\nST,GS,  12,6 kg\n", "12.6", "kg", ",")]
    [InlineData("-3.5\r\n-3.5\r\n-3.5\r\n", "-3.5", null, ".")]
    public void Repeated_ascii_weight_lines_are_recognised(string text, string value, string? unit, string separator)
    {
        var match = new GenericAsciiProbe().Recognise(Encoding.Latin1.GetBytes(text), Now);

        match.ShouldNotBeNull();
        match.ProtocolType.ShouldBe("generic-ascii");
        match.ProtocolSettings["pattern"].ShouldBe(GenericAsciiProbe.ProposedPattern);
        match.ProtocolSettings["decimalSeparator"].ShouldBe(separator);
        match.Reading!.Value.ShouldBe(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        match.Reading.Unit.ShouldBe(unit);

        // The proposal works with the real protocol settings.
        var parser = new GenericAsciiParser(new GenericAsciiOptions { Pattern = GenericAsciiProbe.ProposedPattern, DecimalSeparator = separator });
        parser.Parse(text.Split('\n')[1].TrimEnd('\r'), Now).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("572 g\r\n")]
    [InlineData("hello\r\nworld\r\n")]
    [InlineData("??S ?S\xff 572 g\r\n??S ?S\xff 572 g\r\n")]
    public void Other_output_is_not_taken_for_ascii_weights(string text)
    {
        new GenericAsciiProbe().Recognise(Encoding.Latin1.GetBytes(text), Now).ShouldBeNull();
    }
}
