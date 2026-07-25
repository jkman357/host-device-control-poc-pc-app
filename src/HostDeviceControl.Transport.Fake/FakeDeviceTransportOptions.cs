// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;

namespace HostDeviceControl.Transport.Fake;

/// <summary>
/// Defines deterministic fault-injection controls for the engineering fake
/// device. Zero values disable the corresponding fault.
/// </summary>
public sealed class FakeDeviceTransportOptions
{
    public TimeSpan CommandResponseDelay { get; init; } = TimeSpan.Zero;

    public bool SuppressCommandResponses { get; init; }

    public int DropEveryNthTelemetrySample { get; init; }

    public int CorruptEveryNthTelemetryFrame { get; init; }

    public void Validate()
    {
        if (CommandResponseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(CommandResponseDelay));
        }

        if (DropEveryNthTelemetrySample < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DropEveryNthTelemetrySample));
        }

        if (CorruptEveryNthTelemetryFrame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CorruptEveryNthTelemetryFrame));
        }
    }
}
