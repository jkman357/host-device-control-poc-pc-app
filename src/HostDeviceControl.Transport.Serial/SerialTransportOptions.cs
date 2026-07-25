// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.IO.Ports;

namespace HostDeviceControl.Transport.Serial;

/// <summary>
/// Validated configuration for one Windows serial-port transport instance.
/// </summary>
public sealed class SerialTransportOptions
{
    public const int DefaultBaudRate = 115200;
    public const int DefaultDataBits = 8;

    public SerialTransportOptions(
        string portName,
        int baudRate = DefaultBaudRate,
        Parity parity = Parity.None,
        int dataBits = DefaultDataBits,
        StopBits stopBits = StopBits.One,
        Handshake handshake = Handshake.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        if (baudRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baudRate));
        }

        if ((dataBits < 5) || (dataBits > 8))
        {
            throw new ArgumentOutOfRangeException(nameof(dataBits));
        }

        if (!Enum.IsDefined(parity))
        {
            throw new ArgumentOutOfRangeException(nameof(parity));
        }

        if (!Enum.IsDefined(stopBits) || (stopBits == StopBits.None))
        {
            throw new ArgumentOutOfRangeException(nameof(stopBits));
        }

        if (!Enum.IsDefined(handshake))
        {
            throw new ArgumentOutOfRangeException(nameof(handshake));
        }

        PortName = portName.Trim();
        BaudRate = baudRate;
        Parity = parity;
        DataBits = dataBits;
        StopBits = stopBits;
        Handshake = handshake;
    }

    public string PortName { get; }

    public int BaudRate { get; }

    public Parity Parity { get; }

    public int DataBits { get; }

    public StopBits StopBits { get; }

    public Handshake Handshake { get; }
}
