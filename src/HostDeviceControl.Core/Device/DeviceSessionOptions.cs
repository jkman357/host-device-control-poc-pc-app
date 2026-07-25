// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Threading;
using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Core.Device;

/// <summary>
/// Defines bounded timeout and buffer policies for one device session.
/// Defaults are derived from the shared Project Protocol.
/// </summary>
public sealed class DeviceSessionOptions
{
    public static DeviceSessionOptions Default { get; } = new();

    public TimeSpan GetDeviceInfoTimeout { get; init; } =
        TimeSpan.FromMilliseconds(ProtocolConstants.GetDeviceInfoTimeoutMs);

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

    private static void ValidatePositiveTimeout(TimeSpan timeout, string propertyName)
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
