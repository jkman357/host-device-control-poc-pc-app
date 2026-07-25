// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;

namespace HostDeviceControl.Core.Models;

/// <summary>
/// Immutable telemetry sample containing the Node sequence time and the host
/// UTC receipt timestamp.
/// </summary>
public readonly record struct TelemetrySample(
    uint SampleCounter,
    uint DeviceTickUs,
    float SineValue,
    ushort StatusFlags,
    DateTimeOffset HostReceivedUtc);
