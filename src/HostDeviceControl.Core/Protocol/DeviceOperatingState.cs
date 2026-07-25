// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Node operating states defined by the shared Project Protocol.
/// </summary>
public enum DeviceOperatingState : byte
{
    Idle = 0x00,
    Streaming = 0x01
}
