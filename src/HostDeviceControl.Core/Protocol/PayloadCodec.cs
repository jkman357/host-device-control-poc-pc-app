using System;
using System.Buffers.Binary;
using System.Text;
using HostDeviceControl.Core.Models;

namespace HostDeviceControl.Core.Protocol;

public static class PayloadCodec
{
    private const int AckPayloadLength = 2;
    private const int TelemetryPayloadLength = 14;
    private const int DeviceInfoFixedLength = 8;
    private const int MaximumDeviceNameLength = 32;

    public static byte[] EncodeAck(MessageType requestType, ResultCode resultCode)
    {
        return [(byte)requestType, (byte)resultCode];
    }

    public static (MessageType RequestType, ResultCode ResultCode) DecodeAck(
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length != AckPayloadLength)
        {
            throw new ProtocolException("ACK/NACK payload length must be 2 bytes.");
        }

        return ((MessageType)payload[0], (ResultCode)payload[1]);
    }

    public static byte[] EncodeSetStreamConfig(ushort intervalUs)
    {
        byte[] payload = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, intervalUs);
        return payload;
    }

    public static ushort DecodeSetStreamConfig(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 2)
        {
            throw new ProtocolException(
                "SET_STREAM_CONFIG payload length must be 2 bytes.");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(payload);
    }

    public static byte[] EncodeDeviceInfo(DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        byte[] nameBytes = Encoding.UTF8.GetBytes(deviceInfo.DeviceName);
        if (nameBytes.Length > MaximumDeviceNameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceInfo),
                $"Device name exceeds {MaximumDeviceNameLength} UTF-8 bytes.");
        }

        byte[] payload = new byte[DeviceInfoFixedLength + nameBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), deviceInfo.DeviceType);
        payload[2] = deviceInfo.FirmwareMajor;
        payload[3] = deviceInfo.FirmwareMinor;
        payload[4] = deviceInfo.FirmwarePatch;
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(5, 2),
            deviceInfo.MaximumStreamRateHz);
        payload[7] = checked((byte)nameBytes.Length);
        nameBytes.CopyTo(payload.AsSpan(DeviceInfoFixedLength));
        return payload;
    }

    public static DeviceInfo DecodeDeviceInfo(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < DeviceInfoFixedLength)
        {
            throw new ProtocolException("DEVICE_INFO payload is too short.");
        }

        int nameLength = payload[7];
        if (nameLength > MaximumDeviceNameLength)
        {
            throw new ProtocolException("DEVICE_INFO name length exceeds the limit.");
        }

        if (payload.Length != DeviceInfoFixedLength + nameLength)
        {
            throw new ProtocolException("DEVICE_INFO payload length is inconsistent.");
        }

        ushort deviceType = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
        byte firmwareMajor = payload[2];
        byte firmwareMinor = payload[3];
        byte firmwarePatch = payload[4];
        ushort maximumStreamRateHz = BinaryPrimitives.ReadUInt16LittleEndian(
            payload.Slice(5, 2));
        string deviceName = Encoding.UTF8.GetString(
            payload.Slice(DeviceInfoFixedLength, nameLength));

        return new DeviceInfo(
            deviceType,
            firmwareMajor,
            firmwareMinor,
            firmwarePatch,
            maximumStreamRateHz,
            deviceName);
    }

    public static byte[] EncodeTelemetry(TelemetrySample sample)
    {
        byte[] payload = new byte[TelemetryPayloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), sample.SampleCounter);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), sample.DeviceTickUs);
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(8, 4),
            BitConverter.SingleToInt32Bits(sample.SineValue));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12, 2), sample.StatusFlags);
        return payload;
    }

    public static TelemetrySample DecodeTelemetry(
        ReadOnlySpan<byte> payload,
        DateTimeOffset hostReceivedUtc)
    {
        if (payload.Length != TelemetryPayloadLength)
        {
            throw new ProtocolException(
                "TELEMETRY_SAMPLE payload length must be 14 bytes.");
        }

        uint sampleCounter = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
        uint deviceTickUs = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
        int floatBits = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4));
        float sineValue = BitConverter.Int32BitsToSingle(floatBits);
        ushort statusFlags = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));

        return new TelemetrySample(
            sampleCounter,
            deviceTickUs,
            sineValue,
            statusFlags,
            hostReceivedUtc);
    }
}
