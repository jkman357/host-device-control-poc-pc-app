// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.IO.Ports;
using System.Linq;

namespace HostDeviceControl.Transport.Serial;

/// <summary>
/// Discovers serial-port names and orders conventional Windows COM identifiers
/// numerically.
/// </summary>
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
        const string ComPrefix = "COM";

        if (portName.StartsWith(
                ComPrefix,
                StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(portName.AsSpan(ComPrefix.Length), out int number))
        {
            return number;
        }

        return int.MaxValue;
    }
}
