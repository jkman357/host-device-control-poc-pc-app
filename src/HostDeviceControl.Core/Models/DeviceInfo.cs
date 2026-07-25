// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;

namespace HostDeviceControl.Core.Models;

/// <summary>
/// Immutable device-identification data returned by the Node during handshake.
/// </summary>
public sealed record DeviceInfo
{
    public DeviceInfo(
        ushort deviceType,
        byte firmwareMajor,
        byte firmwareMinor,
        byte firmwarePatch,
        ushort maximumStreamRateHz,
        string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);

        DeviceType = deviceType;
        FirmwareMajor = firmwareMajor;
        FirmwareMinor = firmwareMinor;
        FirmwarePatch = firmwarePatch;
        MaximumStreamRateHz = maximumStreamRateHz;
        DeviceName = deviceName;
    }

    public ushort DeviceType { get; }

    public byte FirmwareMajor { get; }

    public byte FirmwareMinor { get; }

    public byte FirmwarePatch { get; }

    public ushort MaximumStreamRateHz { get; }

    public string DeviceName { get; }

    public string FirmwareVersion =>
        $"{FirmwareMajor}.{FirmwareMinor}.{FirmwarePatch}";
}
