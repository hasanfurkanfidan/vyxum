using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Shouldly;
using Vyxum.Devices.MtSics;
using Vyxum.Devices.Transports;
using Xunit;

namespace Vyxum.Devices.Tests.Transports;

public class TcpTransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Write_and_read_round_trip()
    {
        using var listener = StartListener();
        var accept = listener.AcceptTcpClientAsync(Ct).AsTask();
        await using var transport = Create(listener);
        await transport.OpenAsync(Ct);
        using var remote = await accept;
        var remoteStream = remote.GetStream();

        await transport.WriteAsync(Encoding.ASCII.GetBytes("SI\r\n"), Ct);
        var received = new byte[4];
        await remoteStream.ReadExactlyAsync(received, Ct);
        Encoding.ASCII.GetString(received).ShouldBe("SI\r\n");

        await remoteStream.WriteAsync(Encoding.ASCII.GetBytes("OK\r\n"), Ct);
        var buffer = new byte[16];
        var read = await transport.ReadAsync(buffer, Ct);
        Encoding.ASCII.GetString(buffer, 0, read).ShouldBe("OK\r\n");
    }

    [Fact]
    public async Task Description_is_host_and_port()
    {
        await using var transport = new TcpTransport(new TcpTransportOptions { Host = "10.0.0.5", Port = 4001 });

        transport.Description.ShouldBe("10.0.0.5:4001");
    }

    [Fact]
    public async Task Remote_close_makes_read_return_zero()
    {
        using var listener = StartListener();
        var accept = listener.AcceptTcpClientAsync(Ct).AsTask();
        await using var transport = Create(listener);
        await transport.OpenAsync(Ct);
        var remote = await accept;
        remote.Dispose();

        var read = await transport.ReadAsync(new byte[16], Ct);

        read.ShouldBe(0);
    }

    [Fact]
    public async Task Connect_to_closed_port_fails_within_timeout()
    {
        int port;
        using (var probe = StartListener())
        {
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        await using var transport = new TcpTransport(new TcpTransportOptions { Host = "127.0.0.1", Port = port, ConnectTimeout = TimeSpan.FromSeconds(2) });
        var stopwatch = Stopwatch.StartNew();

        await Should.ThrowAsync<Exception>(() => transport.OpenAsync(Ct));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Cancelling_a_pending_read_throws_operation_canceled()
    {
        using var listener = StartListener();
        var accept = listener.AcceptTcpClientAsync(Ct).AsTask();
        await using var transport = Create(listener);
        await transport.OpenAsync(Ct);
        using var remote = await accept;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var pending = transport.ReadAsync(new byte[16], cts.Token).AsTask();
        await Task.Delay(50, Ct);
        pending.IsCompleted.ShouldBeFalse();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task DiscardInput_drops_waiting_bytes_without_blocking()
    {
        using var listener = StartListener();
        var accept = listener.AcceptTcpClientAsync(Ct).AsTask();
        await using var transport = Create(listener);
        await transport.OpenAsync(Ct);
        using var remote = await accept;
        await remote.GetStream().WriteAsync(Encoding.ASCII.GetBytes("stale\r\n"), Ct);
        await Task.Delay(100, Ct);

        transport.DiscardInput();
        transport.DiscardInput();
        await remote.GetStream().WriteAsync(Encoding.ASCII.GetBytes("fresh\r\n"), Ct);

        var buffer = new byte[16];
        var read = await transport.ReadAsync(buffer, Ct);
        Encoding.ASCII.GetString(buffer, 0, read).ShouldBe("fresh\r\n");
    }

    [Fact]
    public async Task MtSics_protocol_reads_a_stable_weight_over_tcp()
    {
        using var listener = StartListener();
        var scale = RunScaleAsync(listener);
        await using var transport = Create(listener);
        await transport.OpenAsync(Ct);
        var protocol = new MtSicsProtocol(new MtSicsProtocolOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(5),
            ResponseTimeout = TimeSpan.FromSeconds(2),
        });

        WeightReading? reading = null;
        await foreach (var item in protocol.ReadAsync(transport, Ct))
        {
            reading = item;
            break;
        }

        reading.ShouldNotBeNull();
        reading.Value.ShouldBe(572m);
        reading.Unit.ShouldBe("g");
        reading.State.ShouldBe(WeightState.Stable);
        await transport.CloseAsync();
        await scale;
    }

    // A tiny scripted scale: answers the first "SI" with ES, every later one with a stable weight.
    private static async Task RunScaleAsync(TcpListener listener)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var received = new StringBuilder();
            var answers = 0;
            var buffer = new byte[64];
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    return;
                }

                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                while (received.ToString().Contains("SI\r\n", StringComparison.Ordinal))
                {
                    received.Remove(0, received.ToString().IndexOf("SI\r\n", StringComparison.Ordinal) + 4);
                    var answer = answers++ == 0 ? "ES\r\n" : "S S        572 g\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The test closed the connection.
        }
    }

    private static TcpListener StartListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private static TcpTransport Create(TcpListener listener) =>
        new(new TcpTransportOptions { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port });
}
