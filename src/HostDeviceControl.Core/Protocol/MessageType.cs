// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Message identifiers derived from <c>protocol/protocol.yaml</c> v0.1.0.
/// The CI protocol-contract validator prevents independent renumbering.
/// </summary>
public enum MessageType : byte
{
    Ping = 0x01,
    GetDeviceInfo = 0x02,
    SetStreamConfig = 0x03,
    StartStream = 0x04,
    StopStream = 0x05,

    Ack = 0x80,
    Nack = 0x81,
    DeviceInfo = 0x82,
    DeviceStatus = 0x83,

    TelemetrySample = 0x90,
    ErrorReport = 0x91
}
