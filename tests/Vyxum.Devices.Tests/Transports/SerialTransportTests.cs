using System.Text;
using Shouldly;
using Vyxum.Devices.MtSics;
using Vyxum.Devices.Transports;
using Xunit;

namespace Vyxum.Devices.Tests.Transports;

public class SerialTransportTests
{
    private const string PortVariable = "VYXUM_TEST_SERIAL";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Options_have_the_usual_scale_defaults()
    {
        var options = new SerialTransportOptions { PortName = "COM3" };

        options.BaudRate.ShouldBe(9600);
        options.DataBits.ShouldBe(8);
        options.Parity.ShouldBe(SerialParity.None);
        options.StopBits.ShouldBe(SerialStopBits.One);
        options.Handshake.ShouldBe(SerialHandshake.None);
        options.DtrEnable.ShouldBeTrue();
        options.RtsEnable.ShouldBeTrue();
    }

    [Theory]
    [InlineData(SerialParity.None, SerialStopBits.One, "COM3 9600 8N1")]
    [InlineData(SerialParity.Even, SerialStopBits.Two, "COM3 9600 8E2")]
    [InlineData(SerialParity.Odd, SerialStopBits.OnePointFive, "COM3 9600 8O1.5")]
    public async Task Description_shows_port_and_line_settings(SerialParity parity, SerialStopBits stopBits, string expected)
    {
        await using var transport = new SerialTransport(new SerialTransportOptions { PortName = "COM3", Parity = parity, StopBits = stopBits });

        transport.Description.ShouldBe(expected);
    }

    [Fact]
    public async Task Reading_before_open_throws()
    {
        await using var transport = new SerialTransport(new SerialTransportOptions { PortName = "COM3" });

        await Should.ThrowAsync<InvalidOperationException>(async () => await transport.ReadAsync(new byte[8], Ct));
    }

    [Fact]
    public async Task Close_and_discard_are_safe_when_not_open()
    {
        await using var transport = new SerialTransport(new SerialTransportOptions { PortName = "COM3" });

        transport.DiscardInput();
        await transport.CloseAsync();
        await transport.CloseAsync();
    }

    [Fact]
    public void GetPortNames_does_not_throw()
    {
        SerialPorts.GetPortNames().ShouldNotBeNull();
    }

    [Fact]
    public async Task Hardware_scale_answers_SI_with_a_weight()
    {
        var portName = Environment.GetEnvironmentVariable(PortVariable);
        if (string.IsNullOrWhiteSpace(portName))
        {
            Assert.Skip($"Serial hardware tests need the {PortVariable} environment variable (for example COM3) with a MT-SICS scale on that port.");
        }

        await using var transport = new SerialTransport(new SerialTransportOptions { PortName = portName });
        await transport.OpenAsync(Ct);
        transport.DiscardInput();

        // Only a read command is ever sent.
        await transport.WriteAsync(Encoding.ASCII.GetBytes("SI\r\n"), Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var buffer = new byte[128];
        var text = new StringBuilder();
        while (!text.ToString().Contains('\n'))
        {
            var read = await transport.ReadAsync(buffer, timeout.Token);
            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        var response = MtSicsParser.Parse(text.ToString().Split('\n')[0].TrimEnd('\r'));
        response.Kind.ShouldBeOneOf(MtSicsResponseKind.Weight, MtSicsResponseKind.Overload, MtSicsResponseKind.Underload);
    }
}
