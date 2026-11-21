using System.Globalization;
using System.Text;
using Shouldly;
using Vyxum.Devices.ToledoContinuous;
using Xunit;

namespace Vyxum.Devices.Tests.ToledoContinuous;

public class ToledoContinuousParserTests
{
    [Theory]
    [InlineData(0, "001234", "123400", 0)]
    [InlineData(1, "001234", "12340", 0)]
    [InlineData(2, "001234", "1234", 0)]
    [InlineData(3, "001234", "123.4", 1)]
    [InlineData(4, "001234", "12.34", 2)]
    [InlineData(5, "001250", "1.250", 3)]
    [InlineData(6, "001234", "0.1234", 4)]
    [InlineData(7, "123456", "1.23456", 5)]
    public void Decimal_location_is_applied_and_scale_kept(int code, string digits, string expected, int scale)
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { DecimalCode = code, Weight = digits }.Build());

        frame.IsValid.ShouldBeTrue();
        frame.Value.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
        frame.Value!.Value.Scale.ShouldBe((byte)scale);
    }

    [Fact]
    public void Stable_gross_kg_frame()
    {
        var builder = new ToledoFrames { DecimalCode = 4 };
        var frame = ToledoContinuousParser.Parse(builder.Build());

        frame.IsValid.ShouldBeTrue();
        frame.InvalidReason.ShouldBeNull();
        frame.State.ShouldBe(WeightState.Stable);
        frame.Unit.ShouldBe("kg");
        frame.IsNet.ShouldBeFalse();
        frame.Tare.ShouldBe(0.00m);
        frame.Raw.ShouldBe(builder.Payload);
    }

    [Fact]
    public void Negative_sign_is_applied_to_the_weight_only()
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { DecimalCode = 4, Negative = true, Net = true, Tare = "000500" }.Build());

        frame.Value.ShouldBe(-12.34m);
        frame.Tare.ShouldBe(5.00m);
        frame.Tare!.Value.Scale.ShouldBe((byte)2);
    }

    [Fact]
    public void Negative_zero_is_zero()
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { Negative = true, Weight = "000000" }.Build());

        frame.Value.ShouldBe(0m);
        frame.Value!.Value.ToString(CultureInfo.InvariantCulture).ShouldBe("0");
    }

    [Fact]
    public void Motion_bit_gives_motion_state()
    {
        ToledoContinuousParser.Parse(new ToledoFrames { Motion = true }.Build()).State.ShouldBe(WeightState.Motion);
    }

    [Fact]
    public void Out_of_range_positive_is_overload_without_value()
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { OutOfRange = true, Motion = true, Weight = "999999" }.Build());

        frame.IsValid.ShouldBeTrue();
        frame.State.ShouldBe(WeightState.Overload);
        frame.Value.ShouldBeNull();
    }

    [Fact]
    public void Out_of_range_negative_is_underload()
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { OutOfRange = true, Negative = true }.Build());

        frame.State.ShouldBe(WeightState.Underload);
        frame.Value.ShouldBeNull();
    }

    [Fact]
    public void Out_of_range_frame_with_dashes_in_weight_field_is_still_valid()
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { OutOfRange = true, Weight = "------" }.Build());

        frame.IsValid.ShouldBeTrue();
        frame.State.ShouldBe(WeightState.Overload);
    }

    [Theory]
    [InlineData(true, 0, "kg")]
    [InlineData(false, 0, "lb")]
    [InlineData(true, 1, "g")]
    [InlineData(true, 2, "t")]
    [InlineData(false, 3, "oz")]
    [InlineData(false, 4, "ozt")]
    [InlineData(false, 5, "dwt")]
    [InlineData(false, 6, "ton")]
    [InlineData(true, 7, null)]
    public void Unit_comes_from_status_words_b_and_c(bool kg, int extension, string? expected)
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { Kg = kg, UnitExtension = extension }.Build());

        frame.IsValid.ShouldBeTrue();
        frame.Unit.ShouldBe(expected);
    }

    [Fact]
    public void Net_weight_with_tare()
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { DecimalCode = 5, Net = true, Weight = "001250", Tare = "000300" }.Build());

        frame.IsNet.ShouldBeTrue();
        frame.Value.ShouldBe(1.250m);
        frame.Tare.ShouldBe(0.300m);
        frame.Tare!.Value.Scale.ShouldBe((byte)3);
    }

    [Fact]
    public void Leading_spaces_are_allowed_in_number_fields()
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { DecimalCode = 3, Weight = "  1234", Tare = "      " }.Build());

        frame.Value.ShouldBe(123.4m);
        frame.Tare.ShouldBe(0.0m);
    }

    [Fact]
    public void Flags_of_status_words_b_and_c_are_exposed()
    {
        var frame = ToledoContinuousParser.Parse(new ToledoFrames { PowerUpNotZeroed = true, PrintRequest = true, Expanded = true }.Build());

        frame.PowerUpNotZeroed.ShouldBeTrue();
        frame.PrintRequested.ShouldBeTrue();
        frame.ExpandedData.ShouldBeTrue();
    }

    [Fact]
    public void Valid_checksum_is_accepted()
    {
        var bytes = new ToledoFrames().Build(checksum: true);

        ToledoContinuousParser.Parse(bytes, checksumEnabled: true).IsValid.ShouldBeTrue();
        ToledoContinuousParser.ComputeChecksum(bytes.AsSpan(0, bytes.Length - 1)).ShouldBe(bytes[^1]);
    }

    [Fact]
    public void Wrong_checksum_is_rejected()
    {
        var bytes = new ToledoFrames().Build(checksum: true);
        bytes[^1] = (byte)((bytes[^1] + 1) & 0x7F);

        var frame = ToledoContinuousParser.Parse(bytes, checksumEnabled: true);

        frame.IsValid.ShouldBeFalse();
        frame.InvalidReason!.ShouldContain("checksum");
    }

    [Fact]
    public void Corrupted_digit_is_caught_by_the_checksum()
    {
        var bytes = new ToledoFrames().Build(checksum: true);
        bytes[8] = (byte)'9';

        ToledoContinuousParser.Parse(bytes, checksumEnabled: true).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Frame_reader_output_is_parsed_with_its_checksum_trailer()
    {
        var builder = new ToledoFrames { DecimalCode = 4 };
        var bytes = builder.Build(checksum: true);
        var frame = new Frame(bytes[1..^2], [bytes[^1]]);

        var parsed = ToledoContinuousParser.Parse(frame, checksumEnabled: true);

        parsed.IsValid.ShouldBeTrue();
        parsed.Value.ShouldBe(12.34m);
        ToledoContinuousParser.Parse(frame with { Trailer = [] }, checksumEnabled: true).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\u0002\r")]
    [InlineData("hello world, this is no frame")]
    [InlineData("\u0002\"0 0012340000000\n")]
    [InlineData("\u0001\"0 001234000000\r")]
    [InlineData("\u0002A0 001234000000\r")]
    [InlineData("\u0002\"° 001234000000\r")]
    [InlineData("\u0002\"0 0012x4000000\r")]
    [InlineData("\u0002\"0 001234000-00\r")]
    [InlineData("\u0002\"0 0012 4000000\r")]
    public void Garbage_is_invalid_and_does_not_throw(string text)
    {
        var frame = ToledoContinuousParser.Parse(Encoding.Latin1.GetBytes(text));

        frame.IsValid.ShouldBeFalse();
        frame.InvalidReason.ShouldNotBeNullOrEmpty();
        Should.Throw<InvalidOperationException>(() => frame.ToReading(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Sample_frame_parses_with_known_status_bytes()
    {
        // SWA '4' = x2 increment, 0.00; SWB '0' = gross, kg, stable; SWC ' ' = no extension.
        var frame = ToledoContinuousParser.Parse(Encoding.Latin1.GetBytes("\u000240 001250000000\r"));

        frame.IsValid.ShouldBeTrue();
        frame.Value.ShouldBe(12.50m);
        frame.Unit.ShouldBe("kg");
        frame.State.ShouldBe(WeightState.Stable);
    }
}
