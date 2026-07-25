using System;
using System.Buffers.Binary;

namespace HostDeviceControl.Core.Protocol;

public sealed class FrameDecoder
{
    private byte[] _buffer = new byte[4096];
    private int _count;

    public long CrcErrorCount { get; private set; }

    public long FormatErrorCount { get; private set; }

    public long DiscardedByteCount { get; private set; }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        if (data.Length > ProtocolConstants.MaximumBufferedBytes)
        {
            data = data.Slice(data.Length - ProtocolConstants.MaximumBufferedBytes);
            _count = 0;
            FormatErrorCount++;
        }

        int requiredLength = _count + data.Length;
        if (requiredLength > ProtocolConstants.MaximumBufferedBytes)
        {
            int bytesToDiscard = requiredLength - ProtocolConstants.MaximumBufferedBytes;
            Discard(bytesToDiscard);
            FormatErrorCount++;
            requiredLength = _count + data.Length;
        }

        EnsureCapacity(requiredLength);
        data.CopyTo(_buffer.AsSpan(_count));
        _count += data.Length;
    }

    public bool TryRead(out ProtocolFrame? frame)
    {
        frame = null;

        while (true)
        {
            int sofIndex = FindStartOfFrame();
            if (sofIndex < 0)
            {
                PreservePossibleSofPrefix();
                return false;
            }

            if (sofIndex > 0)
            {
                Discard(sofIndex);
            }

            if (_count < ProtocolConstants.MinimumFrameSize)
            {
                return false;
            }

            ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
                _buffer.AsSpan(6, 2));

            if (payloadLength > ProtocolConstants.MaximumPayloadSize)
            {
                FormatErrorCount++;
                Discard(1);
                continue;
            }

            int totalLength = ProtocolConstants.MinimumFrameSize + payloadLength;
            if (_count < totalLength)
            {
                return false;
            }

            int crcInputLength =
                ProtocolConstants.HeaderWithoutSofSize + payloadLength;
            ushort expectedCrc = Crc16Ccitt.Compute(
                _buffer.AsSpan(2, crcInputLength));
            ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(
                _buffer.AsSpan(totalLength - ProtocolConstants.CrcSize, 2));

            if (expectedCrc != receivedCrc)
            {
                CrcErrorCount++;
                Discard(1);
                continue;
            }

            byte version = _buffer[2];
            MessageType messageType = (MessageType)_buffer[3];
            ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(
                _buffer.AsSpan(4, 2));
            byte[] payload = _buffer.AsSpan(8, payloadLength).ToArray();

            frame = new ProtocolFrame(version, messageType, sequence, payload);
            Discard(totalLength, countAsDiscarded: false);
            return true;
        }
    }

    private int FindStartOfFrame()
    {
        for (int index = 0; index < _count - 1; index++)
        {
            if ((_buffer[index] == ProtocolConstants.StartOfFrame0) &&
                (_buffer[index + 1] == ProtocolConstants.StartOfFrame1))
            {
                return index;
            }
        }

        return -1;
    }

    private void PreservePossibleSofPrefix()
    {
        if ((_count > 0) &&
            (_buffer[_count - 1] == ProtocolConstants.StartOfFrame0))
        {
            int discarded = _count - 1;
            _buffer[0] = ProtocolConstants.StartOfFrame0;
            _count = 1;
            DiscardedByteCount += discarded;
        }
        else
        {
            DiscardedByteCount += _count;
            _count = 0;
        }
    }

    private void EnsureCapacity(int requiredLength)
    {
        if (requiredLength <= _buffer.Length)
        {
            return;
        }

        int newLength = _buffer.Length;
        while (newLength < requiredLength)
        {
            newLength *= 2;
        }

        newLength = Math.Min(newLength, ProtocolConstants.MaximumBufferedBytes);
        Array.Resize(ref _buffer, newLength);
    }

    private void Discard(int count, bool countAsDiscarded = true)
    {
        if (count <= 0)
        {
            return;
        }

        if (count >= _count)
        {
            if (countAsDiscarded)
            {
                DiscardedByteCount += _count;
            }

            _count = 0;
            return;
        }

        Buffer.BlockCopy(_buffer, count, _buffer, 0, _count - count);
        _count -= count;

        if (countAsDiscarded)
        {
            DiscardedByteCount += count;
        }
    }
}
