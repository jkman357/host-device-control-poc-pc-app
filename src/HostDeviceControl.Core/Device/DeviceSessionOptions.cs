// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Threading;
using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Core.Device;

/// <summary>
/// Defines protocol-derived timeouts and project-local bounded retry and
/// buffer policies for one device session.
/// </summary>
public sealed class DeviceSessionOptions
{
    public static DeviceSessionOptions Default { get; } = new();

    public TimeSpan GetDeviceInfoTimeout { get; init; } =
        TimeSpan.FromMilliseconds(ProtocolConstants.GetDeviceInfoTimeoutMs);

    public int GetDeviceInfoAttemptCount { get; init; } = 2;

    public TimeSpan GetDeviceInfoRetryDelay { get; init; } =
        TimeSpan.FromMilliseconds(250);

    public TimeSpan CommandTimeout { get; init; } =
        TimeSpan.FromMilliseconds(ProtocolConstants.CommandDefaultTimeoutMs);

    public TimeSpan StopStreamTimeout { get; init; } =
        TimeSpan.FromMilliseconds(ProtocolConstants.StopStreamTimeoutMs);

    public TimeSpan PartialFrameTimeout { get; init; } =
        TimeSpan.FromMilliseconds(ProtocolConstants.PartialFrameTimeoutMs);

    public TimeSpan ReceiveLoopShutdownTimeout { get; init; } =
        TimeSpan.FromSeconds(2);

    public int ReceiveBufferSizeBytes { get; init; } = 1024;

    public void Validate()
    {
        ValidatePositiveTimeout(GetDeviceInfoTimeout, nameof(GetDeviceInfoTimeout));
        ValidateGetDeviceInfoAttemptCount();
        ValidateNonNegativeTimeout(
            GetDeviceInfoRetryDelay,
            nameof(GetDeviceInfoRetryDelay));
        ValidatePositiveTimeout(CommandTimeout, nameof(CommandTimeout));
        ValidatePositiveTimeout(StopStreamTimeout, nameof(StopStreamTimeout));
        ValidatePositiveTimeout(PartialFrameTimeout, nameof(PartialFrameTimeout));
        ValidatePositiveTimeout(
            ReceiveLoopShutdownTimeout,
            nameof(ReceiveLoopShutdownTimeout));

        if ((ReceiveBufferSizeBytes < ProtocolConstants.MinimumFrameSize) ||
            (ReceiveBufferSizeBytes > ProtocolConstants.MaximumBufferedBytes))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReceiveBufferSizeBytes),
                ReceiveBufferSizeBytes,
                "Receive buffer size is outside the supported protocol bounds.");
        }
    }

    private void ValidateGetDeviceInfoAttemptCount()
    {
        if ((GetDeviceInfoAttemptCount < 1) ||
            (GetDeviceInfoAttemptCount > 3))
        {
            throw new ArgumentOutOfRangeException(
                nameof(GetDeviceInfoAttemptCount),
                GetDeviceInfoAttemptCount,
                "GET_DEVICE_INFO attempt count must be between 1 and 3.");
        }
    }

    private static void ValidateNonNegativeTimeout(
        TimeSpan timeout,
        string propertyName)
    {
        if ((timeout < TimeSpan.Zero) || (timeout == Timeout.InfiniteTimeSpan))
        {
            throw new ArgumentOutOfRangeException(
                propertyName,
                timeout,
                "Timeout must be finite and non-negative.");
        }
    }

    private static void ValidatePositiveTimeout(
        TimeSpan timeout,
        string propertyName)
    {
        if ((timeout <= TimeSpan.Zero) || (timeout == Timeout.InfiniteTimeSpan))
        {
            throw new ArgumentOutOfRangeException(
                propertyName,
                timeout,
                "Timeout must be a finite positive duration.");
        }
    }
}
