// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HostDeviceControl.Core.Abstractions;
using HostDeviceControl.Core.Concurrency;
using HostDeviceControl.Core.Device;
using HostDeviceControl.Core.Models;
using HostDeviceControl.Core.Protocol;
using HostDeviceControl.Transport.Fake;

namespace HostDeviceControl.Protocol.Tests;

internal static class Program
{
    private const int TestCommandTimeoutMilliseconds = 60;
    private const int TestStopTimeoutMilliseconds = 100;
    private const int RecoveryTestCommandTimeoutMilliseconds = 1000;
    private const int RecoveryTestStopTimeoutMilliseconds = 1000;
    private const int TestShutdownTimeoutMilliseconds = 2000;
    private const int TestHarnessSignalTimeoutMilliseconds = 2000;
    private const int TestReceiveBufferSizeBytes = 1024;

    private static readonly List<string> Failures = [];

    public static async Task<int> Main()
    {
        PrintEvidenceHeader();
        Run("CRC standard vector", TestCrcStandardVector);
        Run("Frame round trip", TestFrameRoundTrip);
        Run("Known PING vector", TestKnownPingVector);
        Run("Known ACK vector", TestKnownAckVector);
        Run("Fragmented frame", TestFragmentedFrame);
        Run("Noise resynchronization", TestNoiseResynchronization);
        Run("CRC rejection", TestCrcRejection);
        Run("Unknown message rejection", TestUnknownMessageRejection);
        Run("Public frame rejects unknown message", TestPublicFrameRejectsUnknownMessage);
        Run("Permissive unknown message decode", TestPermissiveUnknownMessageDecode);
        Run("Malformed ACK rejection", TestMalformedAckRejection);
        Run("Partial-frame timeout discard", TestPartialFrameTimeoutDiscard);
        Run("Device status codec", TestDeviceStatusCodec);
        Run("Error report codec", TestErrorReportCodec);
        Run("Non-finite telemetry rejection", TestNonFiniteTelemetryRejection);
        Run("Bounded UI buffer policy", TestBoundedDropOldestBuffer);
        await RunAsync("Fake device happy path", TestFakeDeviceSessionAsync);
        await RunAsync("Fake invalid-length NACK", TestFakeInvalidLengthNackAsync);
        await RunAsync("Fake unsupported-version NACK", TestFakeUnsupportedVersionNackAsync);
        await RunAsync("Fake unknown-command NACK", TestFakeUnknownCommandNackAsync);
        await RunAsync("Fake CRC fault injection", TestFakeCrcFaultAsync);
        await RunAsync("Fake sample-loss injection", TestFakeSampleLossAsync);
        await RunAsync("Streaming-state reconnect", TestStreamingStateReconnectAsync);
        await RunAsync("Mismatched ACK correlation", TestMismatchedAckCorrelationAsync);
        await RunAsync("Mismatched NACK correlation", TestMismatchedNackCorrelationAsync);
        await RunAsync("PING state synchronization", TestPingStateSynchronizationAsync);
        await RunAsync("Start cancellation authoritative recovery", TestStartCancellationRecoveryAsync);
        await RunAsync("Stop cancellation authoritative recovery", TestStopCancellationRecoveryAsync);
        await RunAsync("Start timeout authoritative recovery", TestStartTimeoutRecoveryAsync);
        await RunAsync("Stop timeout authoritative recovery", TestStopTimeoutRecoveryAsync);
        await RunAsync("Command timeout", TestCommandTimeoutAsync);
        await RunAsync("Command cancellation", TestCommandCancellationAsync);

        if (Failures.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("All engineering protocol tests passed.");
            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"{Failures.Count} test(s) failed:");
        foreach (string failure in Failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }

    private static void PrintEvidenceHeader()
    {
        Console.WriteLine("HostDeviceControl engineering test evidence");
        Console.WriteLine("Software candidate: 0.3.7");
        string testedCommit =
            Environment.GetEnvironmentVariable("GITHUB_SHA") ??
            "uncommitted-local-package";
        Console.WriteLine($"Tested commit: {testedCommit}");
        Console.WriteLine(
            "Implementation base: " +
            "cf229be58b4ae15969ef447083d9c5982ff19ee7");
        Console.WriteLine("Protocol authority: host-device-control-poc-system@e4aa40b v0.1.0");
        Console.WriteLine($"Runtime: {Environment.Version}");
        Console.WriteLine($"OS: {Environment.OSVersion}");
        Console.WriteLine("Simulator: bounded fake-device profile v0.3.7");
        Console.WriteLine(
            "Evidence scope: software protocol/concurrency behavior only; " +
            "not physical hardware validation.");
        Console.WriteLine();
    }

    private static void TestCrcStandardVector()
    {
        byte[] input = Encoding.ASCII.GetBytes("123456789");
        AssertEqual((ushort)0x29B1, Crc16Ccitt.Compute(input));
    }

    private static void TestFrameRoundTrip()
    {
        var expected = new ProtocolFrame(
            ProtocolConstants.WireVersion,
            MessageType.SetStreamConfig,
            0x1234,
            PayloadCodec.EncodeSetStreamConfig(5000));
        byte[] encoded = FrameEncoder.Encode(expected);

        var decoder = new FrameDecoder();
        decoder.Append(encoded);
        AssertTrue(decoder.TryRead(out ProtocolFrame? actual));
        AssertNotNull(actual);
        AssertEqual(expected.Version, actual!.Version);
        AssertEqual(expected.MessageType, actual.MessageType);
        AssertEqual(expected.Sequence, actual.Sequence);
        AssertSpanEqual(expected.Payload.Span, actual.Payload.Span);
    }

    private static void TestKnownPingVector()
    {
        var frame = new ProtocolFrame(
            ProtocolConstants.WireVersion,
            MessageType.Ping,
            1,
            []);
        string actualHex = Convert.ToHexString(FrameEncoder.Encode(frame));
        AssertEqual("A55A0101010000005597", actualHex);
    }


    private static void TestKnownAckVector()
    {
        var frame = new ProtocolFrame(
            ProtocolConstants.WireVersion,
            MessageType.Ack,
            1,
            PayloadCodec.EncodeCommandResponse(
                MessageType.Ping,
                ResultCode.Ok,
                DeviceOperatingState.Idle));
        string actualHex = Convert.ToHexString(FrameEncoder.Encode(frame));
        AssertEqual("A55A018001000300010000536F", actualHex);
    }

    private static void TestFragmentedFrame()
    {
        byte[] encoded = FrameEncoder.Encode(
            new ProtocolFrame(
                ProtocolConstants.WireVersion,
                MessageType.StartStream,
                7,
                []));
        var decoder = new FrameDecoder();

        for (int index = 0; index < encoded.Length - 1; index++)
        {
            decoder.Append(encoded.AsSpan(index, 1));
            AssertFalse(decoder.TryRead(out _));
        }

        decoder.Append(encoded.AsSpan(encoded.Length - 1, 1));
        AssertTrue(decoder.TryRead(out ProtocolFrame? frame));
        AssertEqual(MessageType.StartStream, frame!.MessageType);
    }

    private static void TestNoiseResynchronization()
    {
        byte[] encoded = FrameEncoder.Encode(
            new ProtocolFrame(
                ProtocolConstants.WireVersion,
                MessageType.StopStream,
                9,
                []));
        byte[] input = [0x00, 0x11, 0xA5, 0x01, .. encoded];
        var decoder = new FrameDecoder();
        decoder.Append(input);

        AssertTrue(decoder.TryRead(out ProtocolFrame? frame));
        AssertEqual(MessageType.StopStream, frame!.MessageType);
        AssertTrue(decoder.DiscardedByteCount >= 4);
    }

    private static void TestCrcRejection()
    {
        byte[] corrupted = FrameEncoder.Encode(
            new ProtocolFrame(
                ProtocolConstants.WireVersion,
                MessageType.Ping,
                10,
                []));
        corrupted[^1] ^= 0x01;

        var decoder = new FrameDecoder();
        decoder.Append(corrupted);
        AssertFalse(decoder.TryRead(out _));
        AssertEqual(1L, decoder.CrcErrorCount);
    }

    private static void TestUnknownMessageRejection()
    {
        byte[] encoded = FrameEncoder.Encode(
            new ProtocolFrame(
                ProtocolConstants.WireVersion,
                MessageType.Ping,
                11,
                []));
        encoded[ProtocolConstants.MessageTypeOffset] = 0x7F;
        RewriteCrc(encoded);

        var decoder = new FrameDecoder();
        decoder.Append(encoded);
        AssertFalse(decoder.TryRead(out _));
        AssertEqual(1L, decoder.UnknownMessageTypeCount);
    }

    private static void TestPublicFrameRejectsUnknownMessage()
    {
        AssertThrows<ArgumentOutOfRangeException>(
            () => _ = new ProtocolFrame(
                ProtocolConstants.WireVersion,
                (MessageType)0x7F,
                12,
                []));
    }

    private static void TestPermissiveUnknownMessageDecode()
    {
        const byte UnknownRequestId = 0x7F;
        const ushort Sequence = 12;
        byte[] encoded = FrameEncoder.Encode(
            new ProtocolFrame(
                ProtocolConstants.WireVersion,
                MessageType.Ping,
                Sequence,
                []));
        encoded[ProtocolConstants.MessageTypeOffset] = UnknownRequestId;
        RewriteCrc(encoded);

        var decoder = new FrameDecoder(allowUnknownMessageTypes: true);
        decoder.Append(encoded);

        AssertTrue(decoder.TryRead(out ProtocolFrame? frame));
        AssertNotNull(frame);
        AssertEqual((MessageType)UnknownRequestId, frame!.MessageType);
        AssertEqual(Sequence, frame.Sequence);
        AssertEqual(1L, decoder.UnknownMessageTypeCount);
        AssertEqual(0L, decoder.FormatErrorCount);
    }

    private static void TestMalformedAckRejection()
    {
        AssertThrows<ProtocolException>(
            () => PayloadCodec.DecodeCommandResponse(
                MessageType.Ack,
                [0x7F, 0x00, 0x00]));
        AssertThrows<ProtocolException>(
            () => PayloadCodec.DecodeCommandResponse(
                MessageType.Ack,
                [(byte)MessageType.Ping, 0x7F, 0x00]));
        AssertThrows<ProtocolException>(
            () => PayloadCodec.DecodeCommandResponse(
                MessageType.Ack,
                [(byte)MessageType.Ping, 0x00, 0x7F]));
    }


    private static void TestPartialFrameTimeoutDiscard()
    {
        var decoder = new FrameDecoder();
        decoder.Append([ProtocolConstants.StartOfFrame0, ProtocolConstants.StartOfFrame1]);
        AssertEqual(2, decoder.BufferedByteCount);
        decoder.DiscardPartialFrame();
        AssertEqual(0, decoder.BufferedByteCount);
        AssertEqual(1L, decoder.PartialFrameTimeoutCount);
    }

    private static void TestDeviceStatusCodec()
    {
        var expected = new DeviceStatus(
            DeviceOperatingState.Streaming,
            DeviceStatusBits.RxOverflowObserved | DeviceStatusBits.UartErrorObserved);
        DeviceStatus actual = PayloadCodec.DecodeDeviceStatus(
            PayloadCodec.EncodeDeviceStatus(expected));
        AssertEqual(expected, actual);
    }

    private static void TestErrorReportCodec()
    {
        var expected = new DeviceErrorReport(0x1234, 0x89ABCDEF);
        DeviceErrorReport actual = PayloadCodec.DecodeErrorReport(
            PayloadCodec.EncodeErrorReport(expected));
        AssertEqual(expected, actual);
    }

    private static void TestNonFiniteTelemetryRejection()
    {
        byte[] payload = new byte[ProtocolConstants.TelemetryPayloadSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(8, sizeof(int)),
            BitConverter.SingleToInt32Bits(float.NaN));
        AssertThrows<ProtocolException>(
            () => PayloadCodec.DecodeTelemetry(payload, DateTimeOffset.UtcNow));
    }

    private static void TestBoundedDropOldestBuffer()
    {
        var buffer = new BoundedDropOldestBuffer<int>(3);
        buffer.Enqueue(1);
        buffer.Enqueue(2);
        buffer.Enqueue(3);
        AssertTrue(buffer.Enqueue(4));

        var values = new List<int>();
        AssertEqual(3, buffer.DrainTo(values, 10));
        AssertEqual(1L, buffer.DroppedItemCount);
        AssertSpanEqual<int>([2, 3, 4], values.ToArray());
    }

    private static async Task TestFakeDeviceSessionAsync()
    {
        await using var transport = new FakeDeviceTransport();
        await using var session = new DeviceSession(transport);
        await CollectSamplesAsync(session, 10);
        AssertEqual(0L, session.CrcErrorCount);
        AssertEqual(0L, session.LostSampleCount);
    }

    private static async Task TestFakeInvalidLengthNackAsync()
    {
        await using var transport = new FakeDeviceTransport();
        await transport.ConnectAsync(CancellationToken.None);
        var request = new ProtocolFrame(
            ProtocolConstants.WireVersion,
            MessageType.Ping,
            21,
            [0x00]);

        await transport.WriteAsync(
            FrameEncoder.Encode(request),
            CancellationToken.None);
        ProtocolFrame response = await ReadFrameAsync(transport);
        AssertEqual(MessageType.Nack, response.MessageType);
        AssertEqual(request.Sequence, response.Sequence);
        CommandResponseStatus status = PayloadCodec.DecodeCommandResponse(
            response.MessageType,
            response.Payload.Span);
        AssertEqual(MessageType.Ping, status.RequestType);
        AssertEqual(ResultCode.InvalidLength, status.ResultCode);
        AssertEqual(DeviceOperatingState.Idle, status.DeviceState);
        await transport.DisconnectAsync(CancellationToken.None);
    }

    private static async Task TestFakeUnsupportedVersionNackAsync()
    {
        await using var transport = new FakeDeviceTransport();
        await transport.ConnectAsync(CancellationToken.None);
        var request = new ProtocolFrame(
            0x02,
            MessageType.Ping,
            22,
            []);

        await transport.WriteAsync(
            FrameEncoder.Encode(request),
            CancellationToken.None);
        ProtocolFrame response = await ReadFrameAsync(transport);
        CommandResponseStatus status = PayloadCodec.DecodeCommandResponse(
            response.MessageType,
            response.Payload.Span);
        AssertEqual(MessageType.Nack, response.MessageType);
        AssertEqual(ResultCode.UnsupportedVersion, status.ResultCode);
        AssertEqual(DeviceOperatingState.Idle, status.DeviceState);
        await transport.DisconnectAsync(CancellationToken.None);
    }

    private static async Task TestFakeUnknownCommandNackAsync()
    {
        const byte UnknownRequestId = 0x7F;
        const ushort Sequence = 23;
        await using var transport = new FakeDeviceTransport();
        await transport.ConnectAsync(CancellationToken.None);
        byte[] requestBytes = FrameEncoder.Encode(
            new ProtocolFrame(
                ProtocolConstants.WireVersion,
                MessageType.Ping,
                Sequence,
                []));
        requestBytes[ProtocolConstants.MessageTypeOffset] = UnknownRequestId;
        RewriteCrc(requestBytes);

        await transport.WriteAsync(requestBytes, CancellationToken.None);
        ProtocolFrame response = await ReadFrameAsync(transport);
        AssertEqual(MessageType.Nack, response.MessageType);
        AssertEqual(Sequence, response.Sequence);
        AssertSpanEqual<byte>(
            [
                UnknownRequestId,
                (byte)ResultCode.InvalidCommand,
                (byte)DeviceOperatingState.Idle
            ],
            response.Payload.Span);
        await transport.DisconnectAsync(CancellationToken.None);
    }

    private static async Task TestFakeCrcFaultAsync()
    {
        var options = new FakeDeviceTransportOptions
        {
            CorruptEveryNthTelemetryFrame = 3
        };
        await using var transport = new FakeDeviceTransport(options);
        await using var session = new DeviceSession(transport);
        await CollectSamplesAsync(session, 10);
        AssertTrue(session.CrcErrorCount > 0);
    }

    private static async Task TestFakeSampleLossAsync()
    {
        var options = new FakeDeviceTransportOptions
        {
            DropEveryNthTelemetrySample = 4
        };
        await using var transport = new FakeDeviceTransport(options);
        await using var session = new DeviceSession(transport);
        await CollectSamplesAsync(session, 12);
        AssertTrue(session.LostSampleCount > 0);
    }


    private static async Task TestStreamingStateReconnectAsync()
    {
        await using var transport = new ScriptedDeviceTransport(
            initialState: DeviceOperatingState.Streaming);
        await using var session = new DeviceSession(transport);

        await session.ConnectAsync(CancellationToken.None);

        AssertEqual(DeviceOperatingState.Streaming, session.DeviceState);
        AssertEqual(DeviceSessionState.Streaming, session.State);
        await session.DisconnectAsync(CancellationToken.None);
    }

    private static async Task TestMismatchedAckCorrelationAsync()
    {
        await using var transport = new ScriptedDeviceTransport(
            mismatchedResponseFor: MessageType.SetStreamConfig);
        await using var session = new DeviceSession(transport);
        await session.ConnectAsync(CancellationToken.None);

        await AssertThrowsAsync<ProtocolException>(
            () => session.StartStreamingAsync(
                ProtocolConstants.DefaultStreamIntervalUs,
                CancellationToken.None));

        AssertEqual(DeviceOperatingState.Idle, session.DeviceState);
        AssertEqual(DeviceSessionState.Ready, session.State);
    }

    private static async Task TestMismatchedNackCorrelationAsync()
    {
        await using var transport = new ScriptedDeviceTransport(
            mismatchedResponseFor: MessageType.SetStreamConfig,
            useNackForMismatch: true);
        await using var session = new DeviceSession(transport);
        await session.ConnectAsync(CancellationToken.None);

        await AssertThrowsAsync<ProtocolException>(
            () => session.StartStreamingAsync(
                ProtocolConstants.DefaultStreamIntervalUs,
                CancellationToken.None));

        AssertEqual(DeviceOperatingState.Idle, session.DeviceState);
        AssertEqual(DeviceSessionState.Ready, session.State);
    }

    private static async Task TestPingStateSynchronizationAsync()
    {
        await using var transport = new ScriptedDeviceTransport();
        await using var session = new DeviceSession(transport);
        await session.ConnectAsync(CancellationToken.None);

        transport.SetAuthoritativeState(DeviceOperatingState.Streaming);
        await session.PingAsync(CancellationToken.None);

        AssertEqual(DeviceOperatingState.Streaming, session.DeviceState);
        AssertEqual(DeviceSessionState.Streaming, session.State);
    }

    private static async Task TestStartCancellationRecoveryAsync()
    {
        await using var transport = new ScriptedDeviceTransport(
            suppressResponseOnceFor: MessageType.StartStream);
        await using var session = new DeviceSession(transport);
        await session.ConnectAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        Task startTask = session.StartStreamingAsync(
            ProtocolConstants.DefaultStreamIntervalUs,
            cancellation.Token);
        await WaitForSuppressedCommandAsync(transport);
        cancellation.Cancel();

        await AssertThrowsAsync<OperationCanceledException>(() => startTask);

        AssertEqual(DeviceOperatingState.Streaming, session.DeviceState);
        AssertEqual(DeviceSessionState.Streaming, session.State);
    }

    private static async Task TestStopCancellationRecoveryAsync()
    {
        await using var transport = new ScriptedDeviceTransport(
            suppressResponseOnceFor: MessageType.StopStream);
        await using var session = new DeviceSession(transport);
        await session.ConnectAsync(CancellationToken.None);
        await session.StartStreamingAsync(
            ProtocolConstants.DefaultStreamIntervalUs,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        Task stopTask = session.StopStreamingAsync(cancellation.Token);
        await WaitForSuppressedCommandAsync(transport);
        cancellation.Cancel();

        await AssertThrowsAsync<OperationCanceledException>(() => stopTask);

        AssertEqual(DeviceOperatingState.Idle, session.DeviceState);
        AssertEqual(DeviceSessionState.Ready, session.State);
    }

    private static async Task TestStartTimeoutRecoveryAsync()
    {
        await using var transport = new ScriptedDeviceTransport(
            suppressResponseOnceFor: MessageType.StartStream);
        await using var session = new DeviceSession(
            transport,
            CreateShortTimeoutOptions());
        await session.ConnectAsync(CancellationToken.None);

        Task startTask = session.StartStreamingAsync(
            ProtocolConstants.DefaultStreamIntervalUs,
            CancellationToken.None);
        await WaitForSuppressedCommandAsync(transport);

        await AssertThrowsAsync<TimeoutException>(() => startTask);

        AssertEqual(DeviceOperatingState.Streaming, session.DeviceState);
        AssertEqual(DeviceSessionState.Streaming, session.State);
    }

    private static async Task TestStopTimeoutRecoveryAsync()
    {
        await using var transport = new ScriptedDeviceTransport(
            suppressResponseOnceFor: MessageType.StopStream);
        await using var session = new DeviceSession(
            transport,
            CreateShortTimeoutOptions());
        await session.ConnectAsync(CancellationToken.None);
        await session.StartStreamingAsync(
            ProtocolConstants.DefaultStreamIntervalUs,
            CancellationToken.None);

        Task stopTask = session.StopStreamingAsync(CancellationToken.None);
        await WaitForSuppressedCommandAsync(transport);

        await AssertThrowsAsync<TimeoutException>(() => stopTask);

        AssertEqual(DeviceOperatingState.Idle, session.DeviceState);
        AssertEqual(DeviceSessionState.Ready, session.State);
    }

    private static async Task WaitForSuppressedCommandAsync(
        ScriptedDeviceTransport transport)
    {
        using var signalTimeout = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(TestHarnessSignalTimeoutMilliseconds));
        await transport.WaitForSuppressedCommandAsync(signalTimeout.Token);
    }

    private static DeviceSessionOptions CreateShortTimeoutOptions()
    {
        return new DeviceSessionOptions
        {
            GetDeviceInfoTimeout = TimeSpan.FromMilliseconds(
                RecoveryTestCommandTimeoutMilliseconds),
            CommandTimeout = TimeSpan.FromMilliseconds(
                RecoveryTestCommandTimeoutMilliseconds),
            StopStreamTimeout = TimeSpan.FromMilliseconds(
                RecoveryTestStopTimeoutMilliseconds),
            ReceiveLoopShutdownTimeout = TimeSpan.FromMilliseconds(
                TestShutdownTimeoutMilliseconds),
            ReceiveBufferSizeBytes = TestReceiveBufferSizeBytes
        };
    }

    private static async Task TestCommandTimeoutAsync()
    {
        var transportOptions = new FakeDeviceTransportOptions
        {
            SuppressCommandResponses = true
        };
        var sessionOptions = new DeviceSessionOptions
        {
            GetDeviceInfoTimeout = TimeSpan.FromMilliseconds(TestCommandTimeoutMilliseconds),
            CommandTimeout = TimeSpan.FromMilliseconds(TestCommandTimeoutMilliseconds),
            StopStreamTimeout = TimeSpan.FromMilliseconds(TestStopTimeoutMilliseconds),
            ReceiveLoopShutdownTimeout = TimeSpan.FromMilliseconds(TestShutdownTimeoutMilliseconds),
            ReceiveBufferSizeBytes = TestReceiveBufferSizeBytes
        };

        await using var transport = new FakeDeviceTransport(transportOptions);
        await using var session = new DeviceSession(transport, sessionOptions);
        await AssertThrowsAsync<TimeoutException>(
            () => session.ConnectAsync(CancellationToken.None));
        AssertEqual(DeviceSessionState.Disconnected, session.State);
    }

    private static async Task TestCommandCancellationAsync()
    {
        var transportOptions = new FakeDeviceTransportOptions
        {
            SuppressCommandResponses = true
        };
        var sessionOptions = new DeviceSessionOptions
        {
            GetDeviceInfoTimeout = TimeSpan.FromSeconds(2),
            CommandTimeout = TimeSpan.FromSeconds(2),
            StopStreamTimeout = TimeSpan.FromMilliseconds(TestStopTimeoutMilliseconds),
            ReceiveLoopShutdownTimeout = TimeSpan.FromMilliseconds(TestShutdownTimeoutMilliseconds),
            ReceiveBufferSizeBytes = TestReceiveBufferSizeBytes
        };

        await using var transport = new FakeDeviceTransport(transportOptions);
        await using var session = new DeviceSession(transport, sessionOptions);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(TestCommandTimeoutMilliseconds));
        await AssertThrowsAsync<OperationCanceledException>(
            () => session.ConnectAsync(cancellation.Token));
        AssertEqual(DeviceSessionState.Disconnected, session.State);
    }

    private static async Task CollectSamplesAsync(
        DeviceSession session,
        int requiredSampleCount)
    {
        int sampleCount = 0;
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        session.TelemetryReceived += OnTelemetry;
        try
        {
            await session.ConnectAsync(CancellationToken.None);
            AssertEqual(DeviceSessionState.Ready, session.State);
            AssertNotNull(session.DeviceInfo);
            AssertEqual(DeviceOperatingState.Idle, session.DeviceState);

            await session.StartStreamingAsync(
                ProtocolConstants.DefaultStreamIntervalUs,
                CancellationToken.None);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
            AssertEqual(DeviceSessionState.Streaming, session.State);
            AssertEqual(DeviceOperatingState.Streaming, session.DeviceState);
            AssertTrue(sampleCount >= requiredSampleCount);

            await session.StopStreamingAsync(CancellationToken.None);
            AssertEqual(DeviceSessionState.Ready, session.State);
            AssertEqual(DeviceOperatingState.Idle, session.DeviceState);
            await session.DisconnectAsync(CancellationToken.None);
            AssertEqual(DeviceSessionState.Disconnected, session.State);
        }
        finally
        {
            session.TelemetryReceived -= OnTelemetry;
        }

        void OnTelemetry(TelemetrySample sample)
        {
            int current = Interlocked.Increment(ref sampleCount);
            if (current >= requiredSampleCount)
            {
                completion.TrySetResult(true);
            }
        }
    }

    private static async Task<ProtocolFrame> ReadFrameAsync(
        FakeDeviceTransport transport)
    {
        var decoder = new FrameDecoder();
        byte[] buffer = new byte[128];
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        while (true)
        {
            int length = await transport.ReadAsync(buffer, cancellation.Token);
            decoder.Append(buffer.AsSpan(0, length));
            if (decoder.TryRead(out ProtocolFrame? frame) && frame is not null)
            {
                return frame;
            }
        }
    }

    private static void RewriteCrc(byte[] frame)
    {
        int payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
            frame.AsSpan(ProtocolConstants.PayloadLengthOffset, sizeof(ushort)));
        int crcInputLength =
            ProtocolConstants.HeaderWithoutSofSize + payloadLength;
        ushort crc = Crc16Ccitt.Compute(
            frame.AsSpan(ProtocolConstants.VersionOffset, crcInputLength));
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(frame.Length - ProtocolConstants.CrcSize),
            crc);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception exception)
        {
            string failure = $"{name}: {exception.Message}";
            Failures.Add(failure);
            Console.Error.WriteLine($"FAIL  {name}: {exception}");
            Console.Error.WriteLine(
                $"::error title=Engineering protocol test failed::" +
                EscapeWorkflowCommand(failure));
        }
    }

    private static async Task RunAsync(string name, Func<Task> test)
    {
        try
        {
            await test();
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception exception)
        {
            string failure = $"{name}: {exception.Message}";
            Failures.Add(failure);
            Console.Error.WriteLine($"FAIL  {name}: {exception}");
            Console.Error.WriteLine(
                $"::error title=Engineering protocol test failed::" +
                EscapeWorkflowCommand(failure));
        }
    }

    private static string EscapeWorkflowCommand(string value)
    {
        return value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
    }

    private static void AssertTrue(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void AssertFalse(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }

    private static void AssertNotNull(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected a non-null value.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected '{expected}', actual '{actual}'.");
        }
    }

    private static void AssertSpanEqual<T>(
        ReadOnlySpan<T> expected,
        ReadOnlySpan<T> actual)
        where T : IEquatable<T>
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException("Sequences are not equal.");
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name}.");
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name}.");
    }

    private sealed class ScriptedDeviceTransport : IDeviceTransport
    {
        private const int ReceiveCapacity = 4096;
        private readonly Channel<byte> _receiveBytes =
            Channel.CreateBounded<byte>(ReceiveCapacity);
        private readonly FrameDecoder _decoder = new();
        private readonly DeviceOperatingState _initialState;
        private readonly MessageType? _mismatchedResponseFor;
        private readonly bool _useNackForMismatch;
        private readonly MessageType? _suppressResponseOnceFor;
        private readonly TaskCompletionSource<bool> _suppressedCommandApplied =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DeviceOperatingState _state;
        private bool _suppressedResponseConsumed;
        private bool _disposed;

        public ScriptedDeviceTransport(
            DeviceOperatingState initialState = DeviceOperatingState.Idle,
            MessageType? mismatchedResponseFor = null,
            bool useNackForMismatch = false,
            MessageType? suppressResponseOnceFor = null)
        {
            _initialState = initialState;
            _state = initialState;
            _mismatchedResponseFor = mismatchedResponseFor;
            _useNackForMismatch = useNackForMismatch;
            _suppressResponseOnceFor = suppressResponseOnceFor;
        }

        public bool IsConnected { get; private set; }

        public void SetAuthoritativeState(DeviceOperatingState state)
        {
            _state = state;
        }

        public Task<bool> WaitForSuppressedCommandAsync(
            CancellationToken cancellationToken)
        {
            if (!_suppressResponseOnceFor.HasValue)
            {
                throw new InvalidOperationException(
                    "No suppressed command is configured for this transport.");
            }

            return _suppressedCommandApplied.Task.WaitAsync(cancellationToken);
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = true;
            _state = _initialState;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = false;
            _receiveBytes.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            byte first = await _receiveBytes.Reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            buffer.Span[0] = first;
            int count = 1;

            while ((count < buffer.Length) &&
                   _receiveBytes.Reader.TryRead(out byte value))
            {
                buffer.Span[count] = value;
                count++;
            }

            return count;
        }

        public async ValueTask WriteAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken)
        {
            _decoder.Append(data.Span);
            if (!_decoder.TryRead(out ProtocolFrame? request) || request is null)
            {
                throw new ProtocolException(
                    "Scripted transport received an incomplete request.");
            }

            ProtocolFrame response = CreateResponse(request);
            if (!_suppressedResponseConsumed &&
                (_suppressResponseOnceFor == request.MessageType))
            {
                _suppressedResponseConsumed = true;
                _suppressedCommandApplied.TrySetResult(true);
                return;
            }

            byte[] encoded = FrameEncoder.Encode(response);
            foreach (byte value in encoded)
            {
                await _receiveBytes.Writer
                    .WriteAsync(value, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private ProtocolFrame CreateResponse(ProtocolFrame request)
        {
            if (request.MessageType == MessageType.GetDeviceInfo)
            {
                var info = new DeviceInfo(
                    0x4460,
                    0,
                    3,
                    2,
                    1000,
                    "Scripted Node");
                return new ProtocolFrame(
                    ProtocolConstants.WireVersion,
                    MessageType.DeviceInfo,
                    request.Sequence,
                    PayloadCodec.EncodeDeviceInfo(info));
            }

            if (_mismatchedResponseFor == request.MessageType)
            {
                MessageType wrongRequest = request.MessageType == MessageType.StartStream
                    ? MessageType.StopStream
                    : MessageType.StartStream;
                MessageType responseType = _useNackForMismatch
                    ? MessageType.Nack
                    : MessageType.Ack;
                ResultCode result = _useNackForMismatch
                    ? ResultCode.InvalidState
                    : ResultCode.Ok;
                return new ProtocolFrame(
                    ProtocolConstants.WireVersion,
                    responseType,
                    request.Sequence,
                    PayloadCodec.EncodeCommandResponse(
                        wrongRequest,
                        result,
                        DeviceOperatingState.Streaming));
            }

            _state = request.MessageType switch
            {
                MessageType.StartStream => DeviceOperatingState.Streaming,
                MessageType.StopStream => DeviceOperatingState.Idle,
                _ => _state
            };

            return new ProtocolFrame(
                ProtocolConstants.WireVersion,
                MessageType.Ack,
                request.Sequence,
                PayloadCodec.EncodeCommandResponse(
                    request.MessageType,
                    ResultCode.Ok,
                    _state));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            if (IsConnected)
            {
                await DisconnectAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            _disposed = true;
        }
    }

}
