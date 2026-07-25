// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Core.Device;

/// <summary>
/// Defines bounded timeout and buffer policies for one device session.
/// </summary>
public sealed class DeviceSessionOptions
{
    /// <summary>
    /// Gets the default immutable option set used by production composition.
    /// </summary>
    public static DeviceSessionOptions Default { get; } = new();

    /// <summary>
    /// Gets or initializes the timeout for normal command responses.
    /// </summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or initializes the timeout for the stop-stream command.
    /// </summary>
    public TimeSpan StopStreamTimeout { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Gets or initializes the maximum time allowed for receive-loop shutdown.
    /// </summary>
    public TimeSpan ReceiveLoopShutdownTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or initializes the transport receive-buffer size in bytes.
    /// </summary>
    public int ReceiveBufferSizeBytes { get; init; } = 1024;

    /// <summary>
    /// Validates all configured bounds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a timeout or buffer size is outside the supported range.
    /// </exception>
    public void Validate()
    {
        ValidatePositiveTimeout(CommandTimeout, nameof(CommandTimeout));
        ValidatePositiveTimeout(StopStreamTimeout, nameof(StopStreamTimeout));
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
