using System.Globalization;
using Shouldly;
using Vyxum.Devices.MtSics;
using Xunit;

namespace Vyxum.Devices.Tests.MtSics;

public class MtSicsParserTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Raw lines recorded from a Mettler Toledo IND231 (9600 8N1, CR LF), see cihaz/ind231-ham.md.
    [Theory]
    [InlineData("S S        572 g", "572")]
    [InlineData("S S        573 g", "573")]
    public void Ind231_stable_weight_lines(string line, string expected)
    {
        var response = MtSicsParser.Parse(line);

        response.Kind.ShouldBe(MtSicsResponseKind.Weight);
        response.IsStable.ShouldBeTrue();
        response.Value.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
        response.Value!.Value.Scale.ShouldBe((byte)0);
        response.Unit.ShouldBe("g");
        response.Raw.ShouldBe(line);
    }

    [Fact]
    public void Ind231_syntax_error_line()
    {
        MtSicsParser.Parse("ES").Kind.ShouldBe(MtSicsResponseKind.SyntaxError);
    }

    [Fact]
    public void Ind231_serial_number_line()
    {
        var response = MtSicsParser.Parse("I4 A \"C139673552\"");

        response.Kind.ShouldBe(MtSicsResponseKind.SerialNumber);
        response.SerialNumber.ShouldBe("C139673552");
    }

    // The same recordings as bytes, through the frame reader, exactly as listed in the hex column.
    [Theory]
    [InlineData("53 20 53 20 20 20 20 20 20 20 20 35 37 32 20 67 0D 0A", MtSicsResponseKind.Weight)]
    [InlineData("45 53 0D 0A", MtSicsResponseKind.SyntaxError)]
    [InlineData("53 20 53 20 20 20 20 20 20 20 20 35 37 33 20 67 0D 0A", MtSicsResponseKind.Weight)]
    [InlineData("49 34 20 41 20 22 43 31 33 39 36 37 33 35 35 32 22 0D 0A", MtSicsResponseKind.SerialNumber)]
    public async Task Ind231_hex_recordings_parse_through_the_frame_reader(string hex, MtSicsResponseKind expected)
    {
        var transport = new FakeTransport();
        transport.Push(Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal)));
        var line = await new FrameReader(transport).ReadLineAsync(Ct);

        MtSicsParser.Parse(line).Kind.ShouldBe(expected);
    }

    // Recorded at the wrong framing (9600 7E1): must not be taken as a weight.
    [Theory]
    [InlineData("??S ?S        572 g")]
    [InlineData("??ES")]
    public void Ind231_garbled_lines_are_unknown(string line)
    {
        MtSicsParser.Parse(line).Kind.ShouldBe(MtSicsResponseKind.Unknown);
    }

    [Theory]
    [InlineData("S D      1.250 kg", false, "1.250", "kg")]
    [InlineData("S S     -12.34 g", true, "-12.34", "g")]
    [InlineData("S D        -0.5 lb", false, "-0.5", "lb")]
    [InlineData("S S      100.00 mg", true, "100.00", "mg")]
    [InlineData("S S       3.125 oz", true, "3.125", "oz")]
    [InlineData("S S       0.050 t", true, "0.050", "t")]
    [InlineData("S S        25.0 ct", true, "25.0", "ct")]
    [InlineData("S S         12 PCS", true, "12", "PCS")]
    [InlineData("  S   S    572   g  ", true, "572", "g")]
    [InlineData("S S 572", true, "572", null)]
    public void Spec_weight_lines_keep_value_scale_and_unit(string line, bool stable, string expected, string? unit)
    {
        var response = MtSicsParser.Parse(line);

        response.Kind.ShouldBe(MtSicsResponseKind.Weight);
        response.IsStable.ShouldBe(stable);
        response.Value!.Value.ToString(CultureInfo.InvariantCulture).ShouldBe(expected);
        response.Unit.ShouldBe(unit);
    }

    [Theory]
    [InlineData("S +", MtSicsResponseKind.Overload)]
    [InlineData("S -", MtSicsResponseKind.Underload)]
    [InlineData("S I", MtSicsResponseKind.Busy)]
    [InlineData(" S  + ", MtSicsResponseKind.Overload)]
    [InlineData("ES", MtSicsResponseKind.SyntaxError)]
    [InlineData("ET", MtSicsResponseKind.TransmissionError)]
    [InlineData("EL", MtSicsResponseKind.LogicalError)]
    [InlineData(" EL ", MtSicsResponseKind.LogicalError)]
    [InlineData("Z A", MtSicsResponseKind.ZeroDone)]
    [InlineData("Z I", MtSicsResponseKind.ZeroNotExecutable)]
    [InlineData("Z +", MtSicsResponseKind.ZeroUpperLimit)]
    [InlineData("Z -", MtSicsResponseKind.ZeroLowerLimit)]
    [InlineData(" Z  A ", MtSicsResponseKind.ZeroDone)]
    [InlineData("Z", MtSicsResponseKind.Unknown)]
    [InlineData("Z X", MtSicsResponseKind.Unknown)]
    [InlineData("Z A 1", MtSicsResponseKind.Unknown)]
    public void Spec_status_and_error_lines(string line, MtSicsResponseKind expected)
    {
        var response = MtSicsParser.Parse(line);

        response.Kind.ShouldBe(expected);
        response.Value.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("S")]
    [InlineData("S X 572 g")]
    [InlineData("S S abc g")]
    [InlineData("S S 1,250 kg")]
    [InlineData("S S 1.2.3 kg")]
    [InlineData("S S 1e3 kg")]
    [InlineData("S S 572 g extra")]
    [InlineData("S Q")]
    [InlineData("ES ES")]
    [InlineData("s s 572 g")]
    [InlineData("I4 A C139673552")]
    [InlineData("I4 I")]
    [InlineData("I4 A \"unterminated")]
    [InlineData("\u0002ÿ\u0000garbage")]
    [InlineData("T S        572 g")]
    public void Garbage_is_unknown_and_never_throws(string? line)
    {
        MtSicsParser.Parse(line).Kind.ShouldBe(MtSicsResponseKind.Unknown);
    }
}
