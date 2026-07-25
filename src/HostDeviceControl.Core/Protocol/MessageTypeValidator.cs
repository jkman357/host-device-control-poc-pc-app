// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Validates message identifiers derived from the authoritative Project
/// Protocol definition.
/// </summary>
public static class MessageTypeValidator
{
    /// <summary>
    /// Returns whether a raw message identifier is defined by protocol v0.1.0.
    /// </summary>
    public static bool IsDefined(byte value)
    {
        return value is
            (byte)MessageType.Ping or
            (byte)MessageType.GetDeviceInfo or
            (byte)MessageType.SetStreamConfig or
            (byte)MessageType.StartStream or
            (byte)MessageType.StopStream or
            (byte)MessageType.Ack or
            (byte)MessageType.Nack or
            (byte)MessageType.DeviceInfo or
            (byte)MessageType.DeviceStatus or
            (byte)MessageType.TelemetrySample or
            (byte)MessageType.ErrorReport;
    }

    /// <summary>
    /// Returns whether a message type is a command defined for PC-to-MCU use.
    /// </summary>
    public static bool IsHostCommand(MessageType messageType)
    {
        return messageType is
            MessageType.Ping or
            MessageType.GetDeviceInfo or
            MessageType.SetStreamConfig or
            MessageType.StartStream or
            MessageType.StopStream;
    }
}
