// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Core.Device;

/// <summary>
/// Represents an explicit NACK returned by the Node for a host command.
/// </summary>
public sealed class DeviceCommandException : Exception
{
    public DeviceCommandException(
        MessageType requestType,
        ResultCode resultCode,
        DeviceOperatingState deviceState,
        string message)
        : base(message)
    {
        RequestType = requestType;
        ResultCode = resultCode;
        DeviceState = deviceState;
    }

    public MessageType RequestType { get; }

    public ResultCode ResultCode { get; }

    public DeviceOperatingState DeviceState { get; }
}
