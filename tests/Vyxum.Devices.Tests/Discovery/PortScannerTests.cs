using System.Diagnostics;
using System.Text;
using Shouldly;
using Vyxum.Devices.Discovery;
using Vyxum.Devices.MtSics;
using Vyxum.Devices.Simulator;
using Vyxum.Devices.Transports;
using Xunit;

namespace Vyxum.Devices.Tests.Discovery;

public class PortScannerTests
{
    private static readonly string[] AllowedCommands = ["SI\r\n", "I4\r\n"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MtSicsScaleSimulator Ind231(bool esFirst = true) =>
        new(new FixedWeightSource(572m, "g"), "C139673552") { FirstCommandReturnsSyntaxError = esFirst };

    private static ToledoContinuousSimulator Toledo(bool checksum = false) =>
        new(new FixedWeightSource(1.25m, "kg", WeightState.Motion), TimeSpan.FromMilliseconds(15), checksum);

    private static void OnlyReadCommandsWereWritten(VirtualPorts ports)
    {
        foreach (var (port, bytes) in ports.Writes)
        {
            AllowedCommands.ShouldContain(Encoding.Latin1.GetString(bytes), $"write to {port}");
        }
    }

    private static Task<PortScanReport> ScanAsync(VirtualPorts ports, IReadOnlyDictionary<string, string>? inUse = null, Action<PortScannerOptions>? configure = null) =>
        new PortScanner(ports.Options(configure)).ScanAsync(new PortScanRequest(inUse ?? new Dictionary<string, string>()), cancellationToken: Ct);

    [Fact]
    public void The_whitelist_is_exactly_SI_and_I4()
    {
        new MtSicsProbe().AllowedWrites.Select(b => Encoding.ASCII.GetString(b)).ShouldBe(AllowedCommands);
        new PortScanner().AllowedWrites.Select(b => Encoding.ASCII.GetString(b)).ShouldBe(AllowedCommands);
        MtSicsCommands.Discovery.ShouldBe(["SI", "I4"]);
    }

    [Theory]
    [InlineData("@\r\n")]
    [InlineData("T\r\n")]
    [InlineData("Z\r\n")]
    [InlineData("S\r\n")]
    [InlineData("SI")]
    [InlineData("SI\r\nT\r\n")]
    public async Task The_guard_refuses_anything_but_the_whitelist(string command)
    {
        var inner = new FakeTransport();
        var guard = new WriteGuardTransport(inner, new MtSicsProbe().AllowedWrites);

        await Should.ThrowAsync<InvalidOperationException>(async () => await guard.WriteAsync(Encoding.ASCII.GetBytes(command), Ct));

        inner.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void The_probe_refuses_to_send_other_mt_sics_commands()
    {
        Should.Throw<InvalidOperationException>(() => MtSicsCommands.EnsureDiscoverySendable("@"));
        Should.Throw<InvalidOperationException>(() => MtSicsCommands.EnsureDiscoverySendable("Z"));
        Should.Throw<InvalidOperationException>(() => MtSicsCommands.EnsureDiscoverySendable("T"));
        MtSicsCommands.EnsureDiscoverySendable("SI");
        MtSicsCommands.EnsureDiscoverySendable("I4");
    }

    [Fact]
    public async Task Mt_sics_scale_is_found_despite_ES_on_the_first_command()
    {
        var device = Ind231(esFirst: true);
        var ports = new VirtualPorts().Device("COM3", device);

        var report = await ScanAsync(ports);

        report.Ports.ShouldHaveSingleItem().ShouldBe(new PortScanStatus("COM3", PortScanState.Found));
        var found = report.Devices.ShouldHaveSingleItem();
        found.PortName.ShouldBe("COM3");
        found.Serial.ShouldNotBeNull();
        found.Serial.BaudRate.ShouldBe(9600);
        found.Serial.DataBits.ShouldBe(8);
        found.Serial.Parity.ShouldBe(SerialParity.None);
        found.Serial.StopBits.ShouldBe(SerialStopBits.One);
        found.Description.ShouldBe("MT-SICS, 9600 8N1");
        found.Match.ProtocolType.ShouldBe("mt-sics");
        found.Match.ProtocolSettings["command"].ShouldBe("SI");
        found.Match.SerialNumber.ShouldBe("C139673552");
        found.Match.Reading.ShouldNotBeNull();
        found.Match.Reading.Value.ShouldBe(572m);
        found.Match.Reading.Unit.ShouldBe("g");
        found.Match.Reading.State.ShouldBe(WeightState.Stable);

        // ES, then the weight; then the serial number. Nothing else reached the device.
        device.ReceivedCommands.ShouldBe(["SI", "SI", "I4"]);
        device.ForbiddenCommands.ShouldBeEmpty();
        OnlyReadCommandsWereWritten(ports);
        ports.Opens.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Device_at_7E1_is_found_after_garbage_at_8N1()
    {
        var device = Ind231(esFirst: false);
        var ports = new VirtualPorts().Device("COM5", device, 9600, SerialFraming.SevenEvenOne);

        var report = await ScanAsync(ports);

        var found = report.Devices.ShouldHaveSingleItem();
        found.Description.ShouldBe("MT-SICS, 9600 7E1");
        found.Serial!.DataBits.ShouldBe(7);
        found.Serial.Parity.ShouldBe(SerialParity.Even);
        ports.Opens.Select(o => (o.BaudRate, o.DataBits, o.Parity)).ShouldBe([(9600, 8, SerialParity.None), (9600, 7, SerialParity.Even)]);
        OnlyReadCommandsWereWritten(ports);
    }

    [Fact]
    public async Task Mt_sics_scale_at_another_baud_rate_is_found()
    {
        var ports = new VirtualPorts().Device("COM7", Ind231(esFirst: false), 19200);

        var report = await ScanAsync(ports);

        report.Devices.ShouldHaveSingleItem().Description.ShouldBe("MT-SICS, 19200 8N1");
        OnlyReadCommandsWereWritten(ports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Toledo_continuous_output_is_recognised_by_listening(bool checksum)
    {
        var ports = new VirtualPorts().Device("COM4", Toledo(checksum), 4800, garbage: [0xFF, 0x80, 0x0D]);

        var report = await ScanAsync(ports);

        var found = report.Devices.ShouldHaveSingleItem();
        found.Description.ShouldBe("Toledo Continuous, 4800 8N1");
        found.Match.ProtocolType.ShouldBe("toledo-continuous");
        found.Match.ProtocolSettings["checksumEnabled"].ShouldBe(checksum);
        found.Match.SerialNumber.ShouldBeNull();
        found.Match.Reading!.Value.ShouldBe(1.25m);
        found.Match.Reading.Unit.ShouldBe("kg");
        found.Match.Reading.State.ShouldBe(WeightState.Motion);
        OnlyReadCommandsWereWritten(ports);
    }

    [Fact]
    public async Task Plain_ascii_output_is_recognised_by_listening()
    {
        var device = new LineReplaySimulator(["ST,GS,+0001.250 kg", "ST,GS,+0001.255 kg"], ReplayMode.Interval, TimeSpan.FromMilliseconds(15));
        var ports = new VirtualPorts().Device("COM6", device, 2400);

        var report = await ScanAsync(ports);

        var found = report.Devices.ShouldHaveSingleItem();
        found.Description.ShouldBe("ASCII, 2400 8N1");
        found.Match.ProtocolType.ShouldBe("generic-ascii");
        found.Match.ProtocolSettings["decimalSeparator"].ShouldBe(".");
        found.Match.Reading!.Unit.ShouldBe("kg");
        found.Match.Reading.Value.ShouldNotBeNull().ShouldBeInRange(1.250m, 1.255m);
        OnlyReadCommandsWereWritten(ports);
    }

    [Fact]
    public async Task Silent_port_is_not_found_within_the_time_bound()
    {
        var ports = new VirtualPorts().Silent("COM9");
        var watch = Stopwatch.StartNew();

        var report = await ScanAsync(ports);

        watch.Stop();
        report.Ports.ShouldHaveSingleItem().State.ShouldBe(PortScanState.NotFound);
        report.Devices.ShouldBeEmpty();

        // 6 baud rates; listening and two SI only with 8N1, one SI with 7E1 and 7O1 (nothing was heard).
        ports.Opens.Count.ShouldBe(18);
        ports.Writes.Count.ShouldBe(6 * (2 + 1 + 1));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        OnlyReadCommandsWereWritten(ports);
    }

    [Fact]
    public async Task Port_used_by_the_agent_is_listed_and_never_opened()
    {
        var device = Ind231();
        var ports = new VirtualPorts().Device("COM3", device).Silent("COM1");

        var report = await ScanAsync(ports, inUse: new Dictionary<string, string> { ["com3"] = "scale-1" });

        report.Ports.ShouldBe([
            new PortScanStatus("COM1", PortScanState.NotFound),
            new PortScanStatus("COM3", PortScanState.InUse, "scale-1"),
        ]);
        report.Devices.ShouldBeEmpty();
        ports.Opens.ShouldAllBe(o => o.PortName == "COM1");
        device.ReceivedCommands.ShouldBeEmpty();
    }

    [Fact]
    public async Task Port_held_by_another_program_is_in_use()
    {
        var ports = new VirtualPorts().HeldByOtherProgram("COM2");

        var report = await ScanAsync(ports);

        var port = report.Ports.ShouldHaveSingleItem();
        port.State.ShouldBe(PortScanState.InUse);
        port.Detail.ShouldBe("The port is used by another program.");
        ports.Opens.Count.ShouldBe(1);
        ports.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Port_that_does_not_open_in_time_is_an_error()
    {
        var ports = new VirtualPorts().Hanging("COM8");

        var report = await ScanAsync(ports, configure: o => o.OpenTimeout = TimeSpan.FromMilliseconds(200));

        var port = report.Ports.ShouldHaveSingleItem();
        port.State.ShouldBe(PortScanState.Error);
        port.Detail.ShouldNotBeNull().ShouldContain("did not open");
    }

    [Fact]
    public async Task Ports_are_scanned_in_parallel_and_progress_is_reported()
    {
        var ports = new VirtualPorts()
            .Device("COM3", Ind231())
            .Device("COM4", Toledo(), 4800)
            .Silent("COM10")
            .Silent("COM11");
        var progress = new SyncProgress();
        var watch = Stopwatch.StartNew();

        var report = await new PortScanner(ports.Options()).ScanAsync(new PortScanRequest(new Dictionary<string, string>()), progress, Ct);

        watch.Stop();
        report.Ports.Select(p => (p.PortName, p.State)).ShouldBe([
            ("COM10", PortScanState.NotFound),
            ("COM11", PortScanState.NotFound),
            ("COM3", PortScanState.Found),
            ("COM4", PortScanState.Found),
        ]);
        report.Devices.Select(d => d.PortName).ShouldBe(["COM3", "COM4"]);

        // Two silent ports one after the other would take twice as long as one.
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(6.5));
        progress.Snapshots.First().ShouldAllBe(p => p.State == PortScanState.Pending);
        progress.Snapshots.ShouldContain(s => s.Any(p => p.State == PortScanState.Scanning));
        progress.Snapshots.Last().ShouldBe(report.Ports);
        OnlyReadCommandsWereWritten(ports);
    }

    [Fact]
    public async Task Scan_can_be_cancelled()
    {
        var ports = new VirtualPorts().Silent("COM1");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() => new PortScanner(ports.Options()).ScanAsync(new PortScanRequest(new Dictionary<string, string>()), null, cts.Token));
    }

    [Fact]
    public async Task Network_address_is_probed_once_with_mt_sics()
    {
        var device = Ind231();
        await using var server = new SimulatorTcpServer(device).Start();
        var options = new PortScannerOptions
        {
            ListenTime = TimeSpan.FromMilliseconds(120),
            ResponseTimeout = TimeSpan.FromMilliseconds(300),
            GetPortNames = () => throw new InvalidOperationException("No serial port may be scanned."),
        };

        var report = await new PortScanner(options).ScanAsync(
            new PortScanRequest(new Dictionary<string, string>(), new TcpTransportOptions { Host = "127.0.0.1", Port = server.Port }), null, Ct);

        report.Ports.ShouldHaveSingleItem().ShouldBe(new PortScanStatus($"127.0.0.1:{server.Port}", PortScanState.Found));
        var found = report.Devices.ShouldHaveSingleItem();
        found.PortName.ShouldBeNull();
        found.Tcp!.Host.ShouldBe("127.0.0.1");
        found.Tcp.Port.ShouldBe(server.Port);
        found.Description.ShouldBe($"MT-SICS, 127.0.0.1:{server.Port}");
        found.Match.SerialNumber.ShouldBe("C139673552");
        device.ReceivedCommands.ShouldBe(["SI", "SI", "I4"]);
    }

    [Fact]
    public async Task Unreachable_network_address_is_an_error()
    {
        var options = new PortScannerOptions { TcpConnectTimeout = TimeSpan.FromSeconds(1) };

        // Port 1 on this computer: nothing listens, the connection is refused.
        var report = await new PortScanner(options).ScanAsync(
            new PortScanRequest(new Dictionary<string, string>(), new TcpTransportOptions { Host = "127.0.0.1", Port = 1 }), null, Ct);

        var port = report.Ports.ShouldHaveSingleItem();
        port.State.ShouldBe(PortScanState.Error);
        port.Detail.ShouldNotBeNullOrEmpty();
        report.Devices.ShouldBeEmpty();
    }

    private sealed class SyncProgress : IProgress<IReadOnlyList<PortScanStatus>>
    {
        private readonly Lock _lock = new();
        private readonly List<IReadOnlyList<PortScanStatus>> _snapshots = [];

        public IReadOnlyList<IReadOnlyList<PortScanStatus>> Snapshots
        {
            get
            {
                lock (_lock)
                {
                    return [.. _snapshots];
                }
            }
        }

        public void Report(IReadOnlyList<PortScanStatus> value)
        {
            lock (_lock)
            {
                _snapshots.Add(value);
            }
        }
    }
}
