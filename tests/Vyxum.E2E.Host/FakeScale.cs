using System.Globalization;
using Vyxum.Devices;
using Vyxum.Devices.Simulator;

namespace Vyxum.E2E.Host;

/// <summary>
/// A simulated MT-SICS scale on a loopback TCP port. The agent reads it like a network scale, so the whole path
/// (agent, hub, live weights, screens) runs. Scenarios change what it shows through <c>POST /e2e/scale</c>.
/// </summary>
internal sealed class FakeScale : IAsyncDisposable
{
    private readonly ControllableWeightSource _source = new();
    private readonly MtSicsScaleSimulator _device;
    private readonly SimulatorTcpServer _server;

    public FakeScale(int port)
    {
        _device = new MtSicsScaleSimulator(_source, "E2E0000001");
        _server = new SimulatorTcpServer(_device, port).Start();
    }

    public int Port => _server.Port;

    /// <summary>
    /// <c>POST /e2e/scale?value=1.25&amp;unit=kg&amp;state=stable|motion|overload|underload</c> changes the weight;
    /// <c>silent=true</c> makes the scale stop answering (a cable pulled out), <c>silent=false</c> brings it back.
    /// <c>GET /e2e/scale</c> returns the current values and the commands the scale received.
    /// </summary>
    public Task HandleAsync(HttpContext context)
    {
        var query = context.Request.Query;
        if (HttpMethods.IsPost(context.Request.Method))
        {
            if (query.ContainsKey("silent"))
                _device.Silent = bool.Parse(query["silent"].ToString());

            var current = _source.Current;
            var value = query.ContainsKey("value") ? decimal.Parse(query["value"].ToString(), CultureInfo.InvariantCulture) : current.Value;
            var unit = query["unit"].ToString() is { Length: > 0 } u ? u : current.Unit;
            var state = query["state"].ToString() is { Length: > 0 } s ? Enum.Parse<WeightState>(s, ignoreCase: true) : current.State;
            _source.Current = new SimulatedWeight(value, unit, state);
        }

        var weight = _source.Current;
        return context.Response.WriteAsJsonAsync(new
        {
            value = weight.Value,
            unit = weight.Unit,
            state = weight.State.ToString(),
            silent = _device.Silent,
            commands = _device.ReceivedCommands.TakeLast(50),
            forbiddenCommands = _device.ForbiddenCommands,
        });
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    private sealed class ControllableWeightSource : IWeightSource
    {
        private readonly Lock _lock = new();
        private SimulatedWeight _current = new(0m, "g", WeightState.Stable);

        public SimulatedWeight Current
        {
            get { lock (_lock) return _current; }
            set { lock (_lock) _current = value; }
        }

        public SimulatedWeight Next() => Current;
    }
}
