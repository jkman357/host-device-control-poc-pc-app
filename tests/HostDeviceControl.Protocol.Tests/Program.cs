using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HostDeviceControl.Core.Device;
using HostDeviceControl.Core.Models;
using HostDeviceControl.Core.Protocol;
using HostDeviceControl.Transport.Fake;

namespace HostDeviceControl.Protocol.Tests;

internal static class Program
{
    private static readonly List<string> Failures = [];

    public static async Task<int> Main()
    {
        Run("CRC standard vector", TestCrcStandardVector);
        Run("Frame round trip", TestFrameRoundTrip);
        Run("Known PING vector", TestKnownPingVector);
        Run("Fragmented frame", TestFragmentedFrame);
        Run("Noise resynchronization", TestNoiseResynchronization);
        Run("CRC rejection", TestCrcRejection);
        await RunAsync("Fake device session", TestFakeDeviceSessionAsync);

        if (Failures.Count == 0)
        {
            Console.WriteLine("All protocol tests passed.");
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
        AssertSequenceEqual(expected.Payload, actual.Payload);
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
        corrupted[3] ^= 0x01;

        var decoder = new FrameDecoder();
        decoder.Append(corrupted);
        AssertFalse(decoder.TryRead(out _));
        AssertEqual(1L, decoder.CrcErrorCount);
    }

    private static async Task TestFakeDeviceSessionAsync()
    {
        await using var transport = new FakeDeviceTransport();
        await using var session = new DeviceSession(transport);
        int sampleCount = 0;
        var sampleCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        session.TelemetryReceived += OnTelemetry;

        void OnTelemetry(TelemetrySample sample)
        {
            int current = Interlocked.Increment(ref sampleCount);
            if (current >= 10)
            {
                sampleCompletion.TrySetResult(true);
            }
        }

        await session.ConnectAsync(CancellationToken.None);
        AssertEqual(DeviceSessionState.Ready, session.State);
        AssertNotNull(session.DeviceInfo);

        await session.StartStreamingAsync(5000, CancellationToken.None);
        await sampleCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(DeviceSessionState.Streaming, session.State);
        AssertTrue(sampleCount >= 10);

        await session.StopStreamingAsync(CancellationToken.None);
        AssertEqual(DeviceSessionState.Ready, session.State);
        await session.DisconnectAsync(CancellationToken.None);
        AssertEqual(DeviceSessionState.Disconnected, session.State);
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
            Failures.Add($"{name}: {exception.Message}");
            Console.Error.WriteLine($"FAIL  {name}");
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
            Failures.Add($"{name}: {exception.Message}");
            Console.Error.WriteLine($"FAIL  {name}");
        }
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

    private static void AssertSequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException("Sequences are not equal.");
        }
    }
}
