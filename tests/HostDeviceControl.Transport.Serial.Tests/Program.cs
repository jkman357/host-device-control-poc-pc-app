// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Collections.Generic;
using System.IO.Ports;
using HostDeviceControl.Core.Protocol;
using HostDeviceControl.Transport.Serial;

namespace HostDeviceControl.Transport.Serial.Tests;

internal static class Program
{
    private static readonly List<string> Failures = [];

    public static int Main()
    {
        PrintEvidenceHeader();
        Run("Supported baud-rate set", TestSupportedBaudRateSet);
        Run("Baud-rate validation", TestBaudRateValidation);
        Run("Fixed UART framing", TestFixedUartFraming);
        Run("Stream-capacity intervals", TestStreamCapacityIntervals);
        Run("Stream-capacity argument validation", TestStreamCapacityValidation);

        if (Failures.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("All serial transport engineering tests passed.");
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
        Console.WriteLine("HostDeviceControl serial transport test evidence");
        Console.WriteLine("Software candidate: 0.3.9");
        string testedCommit =
            Environment.GetEnvironmentVariable("GITHUB_SHA") ??
            "uncommitted-local-package";
        Console.WriteLine($"Tested commit: {testedCommit}");
        Console.WriteLine(
            "Implementation base: " +
            "ec83252f31a82a73b1f621378882361fd06fa941");
        Console.WriteLine(
            "Transport profile: pending system-authority proposal v1");
        Console.WriteLine($"Runtime: {Environment.Version}");
        Console.WriteLine($"OS: {Environment.OSVersion}");
        Console.WriteLine();
    }

    private static void TestSupportedBaudRateSet()
    {
        int[] expected =
        [
            1200,
            2400,
            4800,
            9600,
            19200,
            38400,
            57600,
            115200,
            230400,
            460800,
            921600
        ];

        AssertEqual(expected.Length, SerialTransportOptions.SupportedBaudRates.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            AssertEqual(
                expected[index],
                SerialTransportOptions.SupportedBaudRates[index]);
            AssertTrue(SerialTransportOptions.IsSupportedBaudRate(expected[index]));
        }

        AssertEqual(115200, SerialTransportOptions.DefaultBaudRate);
    }

    private static void TestBaudRateValidation()
    {
        foreach (int baudRate in SerialTransportOptions.SupportedBaudRates)
        {
            var options = new SerialTransportOptions("COM1", baudRate);
            AssertEqual(baudRate, options.BaudRate);
        }

        AssertFalse(SerialTransportOptions.IsSupportedBaudRate(300));
        AssertThrows<ArgumentOutOfRangeException>(
            () => _ = new SerialTransportOptions("COM1", 0));
        AssertThrows<ArgumentOutOfRangeException>(
            () => _ = new SerialTransportOptions("COM1", 300));
        AssertThrows<ArgumentOutOfRangeException>(
            () => _ = new SerialTransportOptions("COM1", 128000));
    }

    private static void TestFixedUartFraming()
    {
        var options = new SerialTransportOptions(" COM7 ", 57600);

        AssertEqual("COM7", options.PortName);
        AssertEqual(8, SerialTransportOptions.RequiredDataBits);
        AssertEqual(Parity.None, SerialTransportOptions.RequiredParity);
        AssertEqual(StopBits.One, SerialTransportOptions.RequiredStopBits);
        AssertEqual(Handshake.None, SerialTransportOptions.RequiredHandshake);
    }

    private static void TestStreamCapacityIntervals()
    {
        AssertUnsupportedForStreaming(1200);
        AssertUnsupportedForStreaming(2400);
        AssertUnsupportedForStreaming(4800);
        AssertStreamInterval(9600, 31250);
        AssertStreamInterval(19200, 15625);
        AssertStreamInterval(38400, 7813);
        AssertStreamInterval(57600, 5209);
        AssertStreamInterval(115200, 5000);
        AssertStreamInterval(230400, 5000);
        AssertStreamInterval(460800, 5000);
        AssertStreamInterval(921600, 5000);
        AssertEqual(24, SerialStreamCapacity.TelemetryFrameSizeBytes);
    }

    private static void TestStreamCapacityValidation()
    {
        AssertThrows<ArgumentOutOfRangeException>(
            () => _ = SerialStreamCapacity.TrySelectStreamIntervalUs(
                300,
                ProtocolConstants.DefaultStreamIntervalUs,
                out _));
        AssertThrows<ArgumentOutOfRangeException>(
            () => _ = SerialStreamCapacity.TrySelectStreamIntervalUs(
                115200,
                999,
                out _));
    }

    private static void AssertUnsupportedForStreaming(int baudRate)
    {
        bool result = SerialStreamCapacity.TrySelectStreamIntervalUs(
            baudRate,
            ProtocolConstants.DefaultStreamIntervalUs,
            out _);
        AssertFalse(result);
    }

    private static void AssertStreamInterval(
        int baudRate,
        ushort expectedIntervalUs)
    {
        bool result = SerialStreamCapacity.TrySelectStreamIntervalUs(
            baudRate,
            ProtocolConstants.DefaultStreamIntervalUs,
            out ushort actualIntervalUs);
        AssertTrue(result);
        AssertEqual(expectedIntervalUs, actualIntervalUs);
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
            string detail = $"{name}: {exception}";
            Failures.Add(detail);
            Console.Error.WriteLine($"FAIL  {detail}");
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

    private static void AssertEqual<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {expected}, actual {actual}.");
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
}
