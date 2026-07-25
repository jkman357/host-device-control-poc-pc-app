// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Core.Models;

/// <summary>
/// Validated ACK or NACK payload returned by the Node.
/// </summary>
public readonly record struct CommandResponseStatus(
    MessageType RequestType,
    ResultCode ResultCode,
    DeviceOperatingState DeviceState);
