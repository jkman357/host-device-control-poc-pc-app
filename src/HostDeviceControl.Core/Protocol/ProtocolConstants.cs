// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Framing, range, and timing values derived from the authoritative Project
/// Protocol definition.
/// </summary>
public static class ProtocolConstants
{
    public const byte StartOfFrame0 = 0xA5;
    public const byte StartOfFrame1 = 0x5A;
    public const byte WireVersion = 0x01;

    public const int StartOfFrameSize = 2;
    public const int HeaderWithoutSofSize = 6;
    public const int CrcSize = 2;
    public const int VersionOffset = 2;
    public const int MessageTypeOffset = 3;
    public const int SequenceOffset = 4;
    public const int PayloadLengthOffset = 6;
    public const int PayloadOffset = 8;
    public const int MinimumFrameSize =
        StartOfFrameSize + HeaderWithoutSofSize + CrcSize;

    public const int MaximumPayloadSize = 1024;
    public const int MaximumBufferedBytes = 65536;
    public const int TelemetryPayloadSize = 14;

    public const ushort DefaultStreamIntervalUs = 5000;
    public const ushort MinimumStreamIntervalUs = 1000;
    public const ushort MaximumStreamIntervalUs = 60000;
}
