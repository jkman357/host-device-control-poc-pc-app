// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Core.Models;

/// <summary>
/// Validated DEVICE_STATUS payload.
/// </summary>
public readonly record struct DeviceStatus(
    DeviceOperatingState State,
    DeviceStatusBits StatusBits);
