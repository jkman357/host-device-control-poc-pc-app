// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Buffers.Binary;

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Encodes validated protocol frames using the Project wire contract.
/// </summary>
public static class FrameEncoder
{
    /// <summary>
    /// Encodes a complete frame, including SOF and CRC-16/CCITT-FALSE.
    /// </summary>
    public static byte[] Encode(ProtocolFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!MessageTypeValidator.IsDefined((byte)frame.MessageType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                frame.MessageType,
                "Message type is not defined by the Project Protocol.");
        }

        if (frame.Payload.Length > ProtocolConstants.MaximumPayloadSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                frame.Payload.Length,
                $"Payload exceeds {ProtocolConstants.MaximumPayloadSize} bytes.");
        }

        int totalLength = ProtocolConstants.MinimumFrameSize + frame.Payload.Length;
        byte[] output = new byte[totalLength];
        Span<byte> span = output;

        span[0] = ProtocolConstants.StartOfFrame0;
        span[1] = ProtocolConstants.StartOfFrame1;
        span[ProtocolConstants.VersionOffset] = frame.Version;
        span[ProtocolConstants.MessageTypeOffset] = (byte)frame.MessageType;
        BinaryPrimitives.WriteUInt16LittleEndian(
            span.Slice(ProtocolConstants.SequenceOffset, sizeof(ushort)),
            frame.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(
            span.Slice(ProtocolConstants.PayloadLengthOffset, sizeof(ushort)),
            checked((ushort)frame.Payload.Length));

        frame.Payload.Span.CopyTo(
            span.Slice(ProtocolConstants.PayloadOffset, frame.Payload.Length));

        int crcInputLength =
            ProtocolConstants.HeaderWithoutSofSize + frame.Payload.Length;
        ushort crc = Crc16Ccitt.Compute(
            span.Slice(ProtocolConstants.VersionOffset, crcInputLength));
        BinaryPrimitives.WriteUInt16LittleEndian(
            span.Slice(totalLength - ProtocolConstants.CrcSize, ProtocolConstants.CrcSize),
            crc);

        return output;
    }
}
