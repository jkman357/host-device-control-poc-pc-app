using System;
using System.Buffers.Binary;

namespace HostDeviceControl.Core.Protocol;

public static class FrameEncoder
{
    public static byte[] Encode(ProtocolFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Payload.Length > ProtocolConstants.MaximumPayloadSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                $"Payload exceeds {ProtocolConstants.MaximumPayloadSize} bytes.");
        }

        int totalLength =
            ProtocolConstants.MinimumFrameSize + frame.Payload.Length;
        byte[] output = new byte[totalLength];
        Span<byte> span = output;

        span[0] = ProtocolConstants.StartOfFrame0;
        span[1] = ProtocolConstants.StartOfFrame1;
        span[2] = frame.Version;
        span[3] = (byte)frame.MessageType;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), frame.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(
            span.Slice(6, 2),
            checked((ushort)frame.Payload.Length));

        frame.Payload.CopyTo(span.Slice(8, frame.Payload.Length));

        int crcInputLength =
            ProtocolConstants.HeaderWithoutSofSize + frame.Payload.Length;
        ushort crc = Crc16Ccitt.Compute(span.Slice(2, crcInputLength));
        BinaryPrimitives.WriteUInt16LittleEndian(
            span.Slice(totalLength - ProtocolConstants.CrcSize, 2),
            crc);

        return output;
    }
}
