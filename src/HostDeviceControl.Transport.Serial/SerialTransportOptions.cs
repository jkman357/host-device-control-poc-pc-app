// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Collections.Generic;
using System.IO.Ports;

namespace HostDeviceControl.Transport.Serial;

/// <summary>
/// Validated configuration for one Windows serial-port transport instance.
/// The controlled transport profile fixes framing to 8-N-1 with no flow
/// control; callers can select only the port name and approved baud rate.
/// </summary>
public sealed class SerialTransportOptions
{
    private static readonly int[] SupportedBaudRateValues =
    [
        1200,
        2400,
        4800,
        9600,
        19200,
        38400,
        57600,
        115200,
        230400,
        460800,
        921600
    ];

    public const int DefaultBaudRate = 115200;
    public const int RequiredDataBits = 8;
    public const Parity RequiredParity = Parity.None;
    public const StopBits RequiredStopBits = StopBits.One;
    public const Handshake RequiredHandshake = Handshake.None;

    public SerialTransportOptions(
        string portName,
        int baudRate = DefaultBaudRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        if (!IsSupportedBaudRate(baudRate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(baudRate),
                baudRate,
                "Baud rate is not in the supported baud-rate set.");
        }

        PortName = portName.Trim();
        BaudRate = baudRate;
    }

    /// <summary>
    /// Gets the baud rates exposed by the application and accepted by this
    /// transport configuration. The physical adapter and target must also
    /// support the selected rate.
    /// </summary>
    public static IReadOnlyList<int> SupportedBaudRates { get; } =
        Array.AsReadOnly(SupportedBaudRateValues);

    /// <summary>
    /// Returns whether the baud rate is part of the controlled host-side
    /// transport proposal. Physical support remains a bring-up responsibility.
    /// </summary>
    public static bool IsSupportedBaudRate(int baudRate)
    {
        return Array.BinarySearch(SupportedBaudRateValues, baudRate) >= 0;
    }

    public string PortName { get; }

    public int BaudRate { get; }

}
