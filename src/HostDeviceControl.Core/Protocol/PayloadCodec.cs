// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Buffers.Binary;
using System.Text;
using HostDeviceControl.Core.Diagnostics;
using HostDeviceControl.Core.Models;

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Encodes and validates protocol payloads owned by
/// <c>protocol/protocol.yaml</c>.
/// </summary>
public static class PayloadCodec
{
    private const int AckPayloadLength = 2;
    private const int StreamConfigPayloadLength = 2;
    private const int TelemetryPayloadLength = ProtocolConstants.TelemetryPayloadSize;
    private const int DeviceInfoFixedLength = 8;
    private const int MaximumDeviceNameLengthBytes = 32;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Encodes an ACK or NACK payload.
    /// </summary>
    public static byte[] EncodeAck(MessageType requestType, ResultCode resultCode)
    {
        if (!MessageTypeValidator.IsHostCommand(requestType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestType),
                requestType,
                "ACK/NACK request type must identify a host command.");
        }

        if (!ResultCodeValidator.IsDefined((byte)resultCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultCode),
                resultCode,
                "Result code is not defined by the Project Protocol.");
        }

        return [(byte)requestType, (byte)resultCode];
    }

    /// <summary>
    /// Decodes and validates an ACK or NACK payload.
    /// </summary>
    public static (MessageType RequestType, ResultCode ResultCode) DecodeAck(
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length != AckPayloadLength)
        {
            throw new ProtocolException("ACK/NACK payload length must be 2 bytes.");
        }

        byte rawRequestType = payload[0];
        byte rawResultCode = payload[1];

        if (!MessageTypeValidator.IsDefined(rawRequestType) ||
            !MessageTypeValidator.IsHostCommand((MessageType)rawRequestType))
        {
            throw new ProtocolException("ACK/NACK request message ID is invalid.");
        }

        if (!ResultCodeValidator.IsDefined(rawResultCode))
        {
            throw new ProtocolException("ACK/NACK result code is invalid.");
        }

        return ((MessageType)rawRequestType, (ResultCode)rawResultCode);
    }

    /// <summary>
    /// Encodes the stream sample interval in microseconds.
    /// </summary>
    public static byte[] EncodeSetStreamConfig(ushort intervalUs)
    {
        ValidateStreamInterval(intervalUs);

        byte[] payload = new byte[StreamConfigPayloadLength];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, intervalUs);
        return payload;
    }

    /// <summary>
    /// Decodes and validates the stream sample interval in microseconds.
    /// </summary>
    public static ushort DecodeSetStreamConfig(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != StreamConfigPayloadLength)
        {
            throw new ProtocolException(
                "SET_STREAM_CONFIG payload length must be 2 bytes.");
        }

        ushort intervalUs = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        ValidateStreamInterval(intervalUs);
        return intervalUs;
    }

    /// <summary>
    /// Encodes device-identification data using strict UTF-8.
    /// </summary>
    public static byte[] EncodeDeviceInfo(DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        string deviceName = DiagnosticText.Sanitize(deviceInfo.DeviceName);
        byte[] nameBytes = StrictUtf8.GetBytes(deviceName);
        if (nameBytes.Length > MaximumDeviceNameLengthBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceInfo),
                $"Device name exceeds {MaximumDeviceNameLengthBytes} UTF-8 bytes.");
        }

        byte[] payload = new byte[DeviceInfoFixedLength + nameBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(0, sizeof(ushort)),
            deviceInfo.DeviceType);
        payload[2] = deviceInfo.FirmwareMajor;
        payload[3] = deviceInfo.FirmwareMinor;
        payload[4] = deviceInfo.FirmwarePatch;
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(5, sizeof(ushort)),
            deviceInfo.MaximumStreamRateHz);
        payload[7] = checked((byte)nameBytes.Length);
        nameBytes.CopyTo(payload.AsSpan(DeviceInfoFixedLength));
        return payload;
    }

    /// <summary>
    /// Decodes device-identification data and rejects malformed UTF-8 or
    /// inconsistent length fields.
    /// </summary>
    public static DeviceInfo DecodeDeviceInfo(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < DeviceInfoFixedLength)
        {
            throw new ProtocolException("DEVICE_INFO payload is too short.");
        }

        int nameLengthBytes = payload[7];
        if (nameLengthBytes > MaximumDeviceNameLengthBytes)
        {
            throw new ProtocolException("DEVICE_INFO name length exceeds the limit.");
        }

        if (payload.Length != DeviceInfoFixedLength + nameLengthBytes)
        {
            throw new ProtocolException("DEVICE_INFO payload length is inconsistent.");
        }

        ushort deviceType = BinaryPrimitives.ReadUInt16LittleEndian(
            payload.Slice(0, sizeof(ushort)));
        byte firmwareMajor = payload[2];
        byte firmwareMinor = payload[3];
        byte firmwarePatch = payload[4];
        ushort maximumStreamRateHz = BinaryPrimitives.ReadUInt16LittleEndian(
            payload.Slice(5, sizeof(ushort)));

        string deviceName;
        try
        {
            deviceName = StrictUtf8.GetString(
                payload.Slice(DeviceInfoFixedLength, nameLengthBytes));
        }
        catch (DecoderFallbackException exception)
        {
            throw new ProtocolException(
                "DEVICE_INFO device name is not valid UTF-8.",
                exception);
        }

        deviceName = DiagnosticText.Sanitize(deviceName);
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ProtocolException("DEVICE_INFO device name is empty.");
        }

        return new DeviceInfo(
            deviceType,
            firmwareMajor,
            firmwareMinor,
            firmwarePatch,
            maximumStreamRateHz,
            deviceName);
    }

    /// <summary>
    /// Encodes one finite telemetry sample.
    /// </summary>
    public static byte[] EncodeTelemetry(TelemetrySample sample)
    {
        if (!float.IsFinite(sample.SineValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sample),
                sample.SineValue,
                "Telemetry value must be finite.");
        }

        byte[] payload = new byte[TelemetryPayloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(0, sizeof(uint)),
            sample.SampleCounter);
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(4, sizeof(uint)),
            sample.DeviceTickUs);
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(8, sizeof(int)),
            BitConverter.SingleToInt32Bits(sample.SineValue));
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(12, sizeof(ushort)),
            sample.StatusFlags);
        return payload;
    }

    /// <summary>
    /// Decodes one telemetry sample and associates it with a host UTC receipt
    /// timestamp supplied by the session owner.
    /// </summary>
    public static TelemetrySample DecodeTelemetry(
        ReadOnlySpan<byte> payload,
        DateTimeOffset hostReceivedUtc)
    {
        if (payload.Length != TelemetryPayloadLength)
        {
            throw new ProtocolException(
                "TELEMETRY_SAMPLE payload length must be 14 bytes.");
        }

        uint sampleCounter = BinaryPrimitives.ReadUInt32LittleEndian(
            payload.Slice(0, sizeof(uint)));
        uint deviceTickUs = BinaryPrimitives.ReadUInt32LittleEndian(
            payload.Slice(4, sizeof(uint)));
        int floatBits = BinaryPrimitives.ReadInt32LittleEndian(
            payload.Slice(8, sizeof(int)));
        float sineValue = BitConverter.Int32BitsToSingle(floatBits);
        ushort statusFlags = BinaryPrimitives.ReadUInt16LittleEndian(
            payload.Slice(12, sizeof(ushort)));

        if (!float.IsFinite(sineValue))
        {
            throw new ProtocolException("TELEMETRY_SAMPLE value is not finite.");
        }

        return new TelemetrySample(
            sampleCounter,
            deviceTickUs,
            sineValue,
            statusFlags,
            hostReceivedUtc);
    }

    private static void ValidateStreamInterval(ushort intervalUs)
    {
        if ((intervalUs < ProtocolConstants.MinimumStreamIntervalUs) ||
            (intervalUs > ProtocolConstants.MaximumStreamIntervalUs))
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalUs),
                intervalUs,
                "Stream interval is outside the Project Protocol range.");
        }
    }
}
