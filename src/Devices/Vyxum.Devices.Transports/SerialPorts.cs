using System.IO.Ports;

namespace Vyxum.Devices.Transports;

/// <summary>Lists the serial ports of this computer.</summary>
public static class SerialPorts
{
    /// <summary>Returns the names of the serial ports that exist now, sorted naturally (COM2 before COM10).</summary>
    public static IReadOnlyList<string> GetPortNames()
    {
        var names = SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        names.Sort(StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, System.Globalization.CompareOptions.NumericOrdering));
        return names;
    }
}
