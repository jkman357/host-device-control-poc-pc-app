// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Validates untrusted device-state values received from the wire.
/// </summary>
public static class DeviceOperatingStateValidator
{
    public static bool IsDefined(byte value) =>
        value is (byte)DeviceOperatingState.Idle or
        (byte)DeviceOperatingState.Streaming;
}
