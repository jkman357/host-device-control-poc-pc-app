// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Buffers.Binary;
using System.Threading;

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Incrementally decodes a bounded byte stream and resynchronizes after noise,
/// malformed length fields, unknown identifiers, and CRC failures.
/// </summary>
public sealed class FrameDecoder
{
    private const int InitialBufferSizeBytes = 4096;

    private readonly bool _allowUnknownMessageTypes;
    private byte[] _buffer = new byte[InitialBufferSizeBytes];
    private int _count;
    private long _crcErrorCount;
    private long _formatErrorCount;
    private long _unknownMessageTypeCount;
    private long _discardedByteCount;
    private long _partialFrameTimeoutCount;


    /// <summary>
    /// Initializes a decoder. The PC receive path rejects unknown IDs; the fake
    /// Node parser may preserve a decodable unknown request so it can return the
    /// Project Protocol INVALID_COMMAND NACK.
    /// </summary>
    public FrameDecoder(bool allowUnknownMessageTypes = false)
    {
        _allowUnknownMessageTypes = allowUnknownMessageTypes;
    }

    public long CrcErrorCount => Interlocked.Read(ref _crcErrorCount);

    public long FormatErrorCount => Interlocked.Read(ref _formatErrorCount);

    public long UnknownMessageTypeCount =>
        Interlocked.Read(ref _unknownMessageTypeCount);

    public long DiscardedByteCount =>
        Interlocked.Read(ref _discardedByteCount);

    public long PartialFrameTimeoutCount =>
        Interlocked.Read(ref _partialFrameTimeoutCount);

    public int BufferedByteCount => _count;

    /// <summary>
    /// Discards an incomplete candidate after the Project Protocol partial-frame
    /// timeout expires.
    /// </summary>
    public void DiscardPartialFrame()
    {
        if (_count == 0)
        {
            return;
        }

        Interlocked.Increment(ref _partialFrameTimeoutCount);
        Interlocked.Increment(ref _formatErrorCount);
        Interlocked.Add(ref _discardedByteCount, _count);
        _count = 0;
    }

    /// <summary>
    /// Appends transport bytes while preserving the protocol-wide memory bound.
    /// </summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        if (data.Length > ProtocolConstants.MaximumBufferedBytes)
        {
            data = data.Slice(data.Length - ProtocolConstants.MaximumBufferedBytes);
            Interlocked.Add(ref _discardedByteCount, _count);
            _count = 0;
            Interlocked.Increment(ref _formatErrorCount);
        }

        int requiredLength = checked(_count + data.Length);
        if (requiredLength > ProtocolConstants.MaximumBufferedBytes)
        {
            int bytesToDiscard =
                requiredLength - ProtocolConstants.MaximumBufferedBytes;
            Discard(bytesToDiscard);
            Interlocked.Increment(ref _formatErrorCount);
            requiredLength = checked(_count + data.Length);
        }

        EnsureCapacity(requiredLength);
        data.CopyTo(_buffer.AsSpan(_count));
        _count += data.Length;
    }

    /// <summary>
    /// Attempts to decode one complete validated frame.
    /// </summary>
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
                _buffer.AsSpan(
                    ProtocolConstants.PayloadLengthOffset,
                    sizeof(ushort)));

            if (payloadLength > ProtocolConstants.MaximumPayloadSize)
            {
                Interlocked.Increment(ref _formatErrorCount);
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
                _buffer.AsSpan(
                    ProtocolConstants.VersionOffset,
                    crcInputLength));
            ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(
                _buffer.AsSpan(
                    totalLength - ProtocolConstants.CrcSize,
                    ProtocolConstants.CrcSize));

            if (expectedCrc != receivedCrc)
            {
                Interlocked.Increment(ref _crcErrorCount);
                Discard(1);
                continue;
            }

            byte rawMessageType = _buffer[ProtocolConstants.MessageTypeOffset];
            bool isKnownMessageType =
                MessageTypeValidator.IsDefined(rawMessageType);

            if (!isKnownMessageType)
            {
                Interlocked.Increment(ref _unknownMessageTypeCount);

                if (!_allowUnknownMessageTypes)
                {
                    Interlocked.Increment(ref _formatErrorCount);
                    Discard(totalLength, countAsDiscarded: false);
                    continue;
                }
            }

            byte version = _buffer[ProtocolConstants.VersionOffset];
            ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(
                _buffer.AsSpan(ProtocolConstants.SequenceOffset, sizeof(ushort)));
            byte[] payload = _buffer
                .AsSpan(ProtocolConstants.PayloadOffset, payloadLength)
                .ToArray();

            frame = ProtocolFrame.CreateDecoded(
                version,
                rawMessageType,
                sequence,
                payload,
                allowUnknownMessageType: !isKnownMessageType);
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
            int discardedByteCount = _count - 1;
            _buffer[0] = ProtocolConstants.StartOfFrame0;
            _count = 1;
            Interlocked.Add(ref _discardedByteCount, discardedByteCount);
        }
        else
        {
            Interlocked.Add(ref _discardedByteCount, _count);
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
            newLength = checked(newLength * 2);
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
                Interlocked.Add(ref _discardedByteCount, _count);
            }

            _count = 0;
            return;
        }

        Buffer.BlockCopy(_buffer, count, _buffer, 0, _count - count);
        _count -= count;

        if (countAsDiscarded)
        {
            Interlocked.Add(ref _discardedByteCount, count);
        }
    }
}
