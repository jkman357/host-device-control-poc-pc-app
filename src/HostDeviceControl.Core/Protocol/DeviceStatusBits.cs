// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Bitwise Node status indicators defined by the shared Project Protocol.
/// </summary>
[Flags]
public enum DeviceStatusBits : ushort
{
    None = 0x0000,
    RxOverflowObserved = 0x0001,
    TxOverflowObserved = 0x0002,
    UartErrorObserved = 0x0004
}
