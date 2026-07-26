// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Transport.Serial;

/// <summary>
/// Applies the host-side UART capacity proposal to telemetry streaming. The
/// calculation reserves line capacity for commands, status, scheduling jitter,
/// and implementation overhead instead of using the theoretical UART limit.
/// </summary>
public static class SerialStreamCapacity
{
    private const int BitsPerUartByte = 10;
    private const int MaximumLineUtilizationPercent = 80;
    private const long MicrosecondsPerSecond = 1_000_000;

    public const int TelemetryFrameSizeBytes =
        ProtocolConstants.MinimumFrameSize +
        ProtocolConstants.TelemetryPayloadSize;

    /// <summary>
    /// Selects the preferred interval when it is safe, otherwise increases the
    /// interval to the minimum safe value. Returns false when even the protocol
    /// maximum interval cannot satisfy the capacity policy.
    /// </summary>
    public static bool TrySelectStreamIntervalUs(
        int baudRate,
        ushort preferredIntervalUs,
        out ushort selectedIntervalUs)
    {
        if (!SerialTransportOptions.IsSupportedBaudRate(baudRate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(baudRate),
                baudRate,
                "Baud rate is not in the supported baud-rate set.");
        }

        if ((preferredIntervalUs < ProtocolConstants.MinimumStreamIntervalUs) ||
            (preferredIntervalUs > ProtocolConstants.MaximumStreamIntervalUs))
        {
            throw new ArgumentOutOfRangeException(nameof(preferredIntervalUs));
        }

        long frameBits = TelemetryFrameSizeBytes * BitsPerUartByte;
        long numerator =
            frameBits * MicrosecondsPerSecond * 100L;
        long denominator =
            (long)baudRate * MaximumLineUtilizationPercent;
        long minimumSafeIntervalUs =
            (numerator + denominator - 1L) / denominator;
        long candidateIntervalUs = Math.Max(
            preferredIntervalUs,
            Math.Max(
                ProtocolConstants.MinimumStreamIntervalUs,
                minimumSafeIntervalUs));

        if (candidateIntervalUs > ProtocolConstants.MaximumStreamIntervalUs)
        {
            selectedIntervalUs = default;
            return false;
        }

        selectedIntervalUs = checked((ushort)candidateIntervalUs);
        return true;
    }
}
