using System;

namespace HostDeviceControl.Core.Models;

public readonly record struct TelemetrySample(
    uint SampleCounter,
    uint DeviceTickUs,
    float SineValue,
    ushort StatusFlags,
    DateTimeOffset HostReceivedUtc);
