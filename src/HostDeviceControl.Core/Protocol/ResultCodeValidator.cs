// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Validates result-code values derived from the authoritative Project
/// Protocol definition.
/// </summary>
public static class ResultCodeValidator
{
    /// <summary>
    /// Returns whether a raw result code is defined by protocol v0.1.0.
    /// </summary>
    public static bool IsDefined(byte value)
    {
        return value is
            (byte)ResultCode.Ok or
            (byte)ResultCode.InvalidCommand or
            (byte)ResultCode.InvalidLength or
            (byte)ResultCode.InvalidValue or
            (byte)ResultCode.InvalidState or
            (byte)ResultCode.UnsupportedVersion or
            (byte)ResultCode.InternalError;
    }
}
