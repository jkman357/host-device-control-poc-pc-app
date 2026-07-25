using System;
using System.IO.Ports;
using System.Linq;

namespace HostDeviceControl.Transport.Serial;

public static class SerialPortDiscovery
{
    public static string[] GetPortNames()
    {
        return SerialPort.GetPortNames()
            .OrderBy(GetPortSortKey)
            .ThenBy(portName => portName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetPortSortKey(string portName)
    {
        if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(portName.AsSpan(3), out int number))
        {
            return number;
        }

        return int.MaxValue;
    }
}
