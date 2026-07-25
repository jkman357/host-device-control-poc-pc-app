// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

namespace HostDeviceControl.Core.Models;

/// <summary>
/// Validated ERROR_REPORT payload emitted by the Node.
/// </summary>
public readonly record struct DeviceErrorReport(
    ushort ErrorCode,
    uint Detail);
