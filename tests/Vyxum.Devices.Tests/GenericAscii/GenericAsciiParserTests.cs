using System.Diagnostics;
using Shouldly;
using Vyxum.Devices.GenericAscii;
using Xunit;

namespace Vyxum.Devices.Tests.GenericAscii;

public class GenericAsciiParserTests
{
    /// <summary>Continuous output common to A&amp;D and many indicators: ST/US/OL, GS/NT, signed value, unit.</summary>
    internal static readonly GenericAsciiOptions HeaderStyle = new()
    {
        Pattern = @"^(?<status>(?:ST|US|OL),(?:GS|NT)),(?<weight>[+-]?[0-9. ]+?)\s*(?<unit>[A-Za-z]*)\s*$",
        StablePattern = "^ST",
        MotionPattern = "^US",
        OverloadPattern = "^OL",
        NetPattern = ",NT$",
    };

    /// <summary>Comma-decimal output with a leading '?' or 'M' while the weight moves.</summary>
    internal static readonly GenericAsciiOptions CommaStyle = new()
    {
        Pattern = @"^(?<status>[?M]?)\s*(?<sign>-?)\s*(?<weight>[0-9.,]+)\s*(?<unit>[a-zA-Z]*)\s*$",
        DecimalSeparator = ",",
        ThousandsSeparator = ".",
        MotionPattern = "[?M]",
        DefaultUnit = "kg",
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("ST,GS,+0001.250kg", "1.250", 3, WeightState.Stable, false)]
    [InlineData("US,GS,+0001.300kg", "1.300", 3, WeightState.Motion, false)]
    [InlineData("ST,NT,-0000.125kg", "-0.125", 3, WeightState.Stable, true)]
    [InlineData("ST,GS,+  12.5 kg", "12.5", 1, WeightState.Stable, false)]
    public void Header_style_lines(string line, string expected, int scale, WeightState state, bool net)
    {
        var reading = new GenericAsciiParser(HeaderStyle).Parse(line, Now).ShouldNotBeNull();

        reading.Value.ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
        reading.Value!.Value.Scale.ShouldBe((byte)scale);
        reading.Unit.ShouldBe("kg");
        reading.State.ShouldBe(state);
        reading.IsNet.ShouldBe(net);
        reading.Tare.ShouldBeNull();
        reading.Raw.ShouldBe(line);
        reading.Timestamp.ShouldBe(Now);
    }

    [Fact]
    public void Header_style_overload_has_no_value()
    {
        var reading = new GenericAsciiParser(HeaderStyle).Parse("OL,GS,+9999.999kg", Now).ShouldNotBeNull();

        reading.State.ShouldBe(WeightState.Overload);
        reading.Value.ShouldBeNull();
        reading.Unit.ShouldBe("kg");
    }

    [Theory]
    [InlineData("     12,345 kg", "12.345", WeightState.Stable)]
    [InlineData("?    12,345 kg", "12.345", WeightState.Motion)]
    [InlineData("M    12,345 kg", "12.345", WeightState.Motion)]
    [InlineData("  1.234,5 kg", "1234.5", WeightState.Stable)]
    [InlineData("  -  0,250 kg", "-0.250", WeightState.Stable)]
    public void Comma_style_lines(string line, string expected, WeightState state)
    {
        var reading = new GenericAsciiParser(CommaStyle).Parse(line, Now).ShouldNotBeNull();

        reading.Value.ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
        reading.Value!.Value.Scale.ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture).Scale);
        reading.State.ShouldBe(state);
        reading.Unit.ShouldBe("kg");
        reading.IsNet.ShouldBeNull();
    }

    [Fact]
    public void Default_unit_is_used_when_the_line_has_none()
    {
        new GenericAsciiParser(CommaStyle).Parse("   5,00", Now)!.Unit.ShouldBe("kg");
        new GenericAsciiParser(CommaStyle).Parse("   5,00 g", Now)!.Unit.ShouldBe("g");
    }

    [Fact]
    public void Comma_decimal_refuses_a_dot_that_is_not_a_thousands_separator()
    {
        var parser = new GenericAsciiParser(CommaStyle with { ThousandsSeparator = null });

        parser.Parse("  1.234 kg", Now).ShouldBeNull();
    }

    [Fact]
    public void Thousands_separator_with_dot_decimal()
    {
        var parser = new GenericAsciiParser(new GenericAsciiOptions { Pattern = @"^(?<weight>[0-9,.]+)$", ThousandsSeparator = "," });

        parser.Parse("12,345.60", Now)!.Value.ShouldBe(12345.60m);
        parser.Parse("12,345.60", Now)!.Value!.Value.Scale.ShouldBe((byte)2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("ST,GS,")]
    [InlineData("XX,GS,+0001.250kg")]
    [InlineData("ST,GS,+1.2.3kg")]
    public void Lines_without_a_usable_weight_are_skipped(string line)
    {
        new GenericAsciiParser(HeaderStyle).Parse(line, Now).ShouldBeNull();
    }

    [Fact]
    public void Without_stability_rules_every_weight_is_stable()
    {
        var parser = new GenericAsciiParser(new GenericAsciiOptions { Pattern = @"(?<weight>-?\d+(\.\d+)?)" });

        var reading = parser.Parse("W: -3.50", Now)!;

        reading.State.ShouldBe(WeightState.Stable);
        reading.Value.ShouldBe(-3.50m);
        reading.Unit.ShouldBeNull();
    }

    [Fact]
    public void With_only_a_stable_rule_an_unmatched_line_is_motion()
    {
        var parser = new GenericAsciiParser(new GenericAsciiOptions { Pattern = @"(?<weight>\d+)", StablePattern = "S$" });

        parser.Parse("100 S", Now)!.State.ShouldBe(WeightState.Stable);
        parser.Parse("100 D", Now)!.State.ShouldBe(WeightState.Motion);
    }

    [Fact]
    public void Status_rules_test_the_whole_line_when_there_is_no_status_group()
    {
        var parser = new GenericAsciiParser(new GenericAsciiOptions { Pattern = @"(?<weight>\d+)", UnderloadPattern = "UL" });

        var reading = parser.Parse("UL 0000", Now)!;

        reading.State.ShouldBe(WeightState.Underload);
        reading.Value.ShouldBeNull();
    }

    [Fact]
    public void Slow_pattern_times_out_and_the_line_is_skipped()
    {
        var parser = new GenericAsciiParser(new GenericAsciiOptions { Pattern = "^(?<weight>(a+)+)$" });
        var clock = Stopwatch.StartNew();

        parser.Parse(new string('a', 40) + "!", Now).ShouldBeNull();

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("(?<weight>")]
    [InlineData(@"(?<value>\d+)")]
    public void Invalid_pattern_is_rejected(string pattern)
    {
        Should.Throw<ArgumentException>(() => new GenericAsciiParser(new GenericAsciiOptions { Pattern = pattern }));
    }

    [Fact]
    public void Invalid_status_pattern_is_rejected()
    {
        var ex = Should.Throw<ArgumentException>(() => new GenericAsciiParser(HeaderStyle with { MotionPattern = "[" }));
        ex.ParamName.ShouldBe("MotionPattern");
    }
}
