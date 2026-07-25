// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Buffers.Binary;
using System.Text;
using HostDeviceControl.Core.Diagnostics;
using HostDeviceControl.Core.Models;

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Encodes and validates payloads derived from the shared Project Protocol.
/// </summary>
public static class PayloadCodec
{
    private const int StreamConfigPayloadLength = 2;
    private const int DeviceInfoFixedLength = 8;
    private const int MaximumDeviceNameLengthBytes = 32;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Encodes a validated ACK or NACK payload.
    /// </summary>
    public static byte[] EncodeCommandResponse(
        MessageType requestType,
        ResultCode resultCode,
        DeviceOperatingState deviceState)
    {
        if (!MessageTypeValidator.IsHostCommand(requestType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestType),
                requestType,
                "Response request type must identify a host command.");
        }

        if (!ResultCodeValidator.IsDefined((byte)resultCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultCode),
                resultCode,
                "Result code is not defined by the Project Protocol.");
        }

        if (!DeviceOperatingStateValidator.IsDefined((byte)deviceState))
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceState),
                deviceState,
                "Device state is not defined by the Project Protocol.");
        }

        return [(byte)requestType, (byte)resultCode, (byte)deviceState];
    }

    /// <summary>
    /// Encodes INVALID_COMMAND or another non-OK NACK for a decodable raw
    /// request message ID that is not part of the current command enum.
    /// </summary>
    public static byte[] EncodeRawNack(
        byte requestMessageId,
        ResultCode resultCode,
        DeviceOperatingState deviceState)
    {
        if (!ResultCodeValidator.IsDefined((byte)resultCode) ||
            (resultCode == ResultCode.Ok))
        {
            throw new ArgumentOutOfRangeException(nameof(resultCode));
        }

        if (!DeviceOperatingStateValidator.IsDefined((byte)deviceState))
        {
            throw new ArgumentOutOfRangeException(nameof(deviceState));
        }

        return [requestMessageId, (byte)resultCode, (byte)deviceState];
    }

    /// <summary>
    /// Decodes and validates an ACK or NACK payload.
    /// </summary>
    public static CommandResponseStatus DecodeCommandResponse(
        MessageType responseType,
        ReadOnlySpan<byte> payload)
    {
        if (responseType is not MessageType.Ack and not MessageType.Nack)
        {
            throw new ArgumentOutOfRangeException(
                nameof(responseType),
                responseType,
                "Response type must be ACK or NACK.");
        }

        if (payload.Length != ProtocolConstants.CommandResponsePayloadSize)
        {
            throw new ProtocolException("ACK/NACK payload length must be 3 bytes.");
        }

        byte rawRequestType = payload[0];
        byte rawResultCode = payload[1];
        byte rawDeviceState = payload[2];

        if (!MessageTypeValidator.IsDefined(rawRequestType) ||
            !MessageTypeValidator.IsHostCommand((MessageType)rawRequestType))
        {
            throw new ProtocolException("ACK/NACK request message ID is invalid.");
        }

        if (!ResultCodeValidator.IsDefined(rawResultCode))
        {
            throw new ProtocolException("ACK/NACK result code is invalid.");
        }

        if (!DeviceOperatingStateValidator.IsDefined(rawDeviceState))
        {
            throw new ProtocolException("ACK/NACK device state is invalid.");
        }

        var resultCode = (ResultCode)rawResultCode;
        if ((responseType == MessageType.Ack) && (resultCode != ResultCode.Ok))
        {
            throw new ProtocolException("ACK result code must be OK.");
        }

        if ((responseType == MessageType.Nack) && (resultCode == ResultCode.Ok))
        {
            throw new ProtocolException("NACK result code must not be OK.");
        }

        return new CommandResponseStatus(
            (MessageType)rawRequestType,
            resultCode,
            (DeviceOperatingState)rawDeviceState);
    }

    public static byte[] EncodeSetStreamConfig(ushort intervalUs)
    {
        ValidateStreamInterval(intervalUs);
        byte[] payload = new byte[StreamConfigPayloadLength];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, intervalUs);
        return payload;
    }

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

    public static byte[] EncodeDeviceStatus(DeviceStatus status)
    {
        if (!DeviceOperatingStateValidator.IsDefined((byte)status.State))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        byte[] payload = new byte[ProtocolConstants.DeviceStatusPayloadSize];
        payload[0] = (byte)status.State;
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(1, sizeof(ushort)),
            (ushort)status.StatusBits);
        return payload;
    }

    public static DeviceStatus DecodeDeviceStatus(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != ProtocolConstants.DeviceStatusPayloadSize)
        {
            throw new ProtocolException("DEVICE_STATUS payload length must be 3 bytes.");
        }

        if (!DeviceOperatingStateValidator.IsDefined(payload[0]))
        {
            throw new ProtocolException("DEVICE_STATUS state is invalid.");
        }

        return new DeviceStatus(
            (DeviceOperatingState)payload[0],
            (DeviceStatusBits)BinaryPrimitives.ReadUInt16LittleEndian(
                payload.Slice(1, sizeof(ushort))));
    }

    public static byte[] EncodeTelemetry(TelemetrySample sample)
    {
        if (!float.IsFinite(sample.SineValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sample),
                sample.SineValue,
                "Telemetry value must be finite.");
        }

        byte[] payload = new byte[ProtocolConstants.TelemetryPayloadSize];
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

    public static TelemetrySample DecodeTelemetry(
        ReadOnlySpan<byte> payload,
        DateTimeOffset hostReceivedUtc)
    {
        if (payload.Length != ProtocolConstants.TelemetryPayloadSize)
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

    public static byte[] EncodeErrorReport(DeviceErrorReport report)
    {
        byte[] payload = new byte[ProtocolConstants.ErrorReportPayloadSize];
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(0, sizeof(ushort)),
            report.ErrorCode);
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(2, sizeof(uint)),
            report.Detail);
        return payload;
    }

    public static DeviceErrorReport DecodeErrorReport(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != ProtocolConstants.ErrorReportPayloadSize)
        {
            throw new ProtocolException("ERROR_REPORT payload length must be 6 bytes.");
        }

        return new DeviceErrorReport(
            BinaryPrimitives.ReadUInt16LittleEndian(
                payload.Slice(0, sizeof(ushort))),
            BinaryPrimitives.ReadUInt32LittleEndian(
                payload.Slice(2, sizeof(uint))));
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
