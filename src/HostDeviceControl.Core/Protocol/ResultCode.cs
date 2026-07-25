// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Result-code values derived from <c>protocol/protocol.yaml</c> v0.1.0.
/// </summary>
public enum ResultCode : byte
{
    Ok = 0x00,
    InvalidCommand = 0x01,
    InvalidLength = 0x02,
    InvalidValue = 0x03,
    InvalidState = 0x04,
    UnsupportedVersion = 0x05,
    InternalError = 0x06
}
