using System;
using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Core.Device;

public sealed class DeviceCommandException : Exception
{
    public DeviceCommandException(
        MessageType requestType,
        ResultCode resultCode,
        string message)
        : base(message)
    {
        RequestType = requestType;
        ResultCode = resultCode;
    }

    public MessageType RequestType { get; }

    public ResultCode ResultCode { get; }
}
