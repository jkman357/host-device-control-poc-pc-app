<!-- Copyright © 2026 Ray Yang. All rights reserved. No license is granted. -->

# PC Application Architecture

## Purpose

This document defines the responsibility and dependency boundaries for the single-Node Windows Coordinator PoC.

## Dependency direction

```text
HostDeviceControl.App
  -> HostDeviceControl.Core
  -> HostDeviceControl.Transport.Fake
  -> HostDeviceControl.Transport.Serial

Transport.Fake / Transport.Serial
  -> HostDeviceControl.Core abstractions and protocol models

HostDeviceControl.Core
  -> no WPF and no SerialPort dependency
```

## Runtime flow

```text
bounded transport bytes
  -> generation-owned DeviceSession receive loop
  -> bounded FrameDecoder and external-value validation
  -> correlated command response OR validated telemetry
  -> bounded UI buffer + bounded recorder queue
  -> 50 ms WPF batch + asynchronous CSV writer
```

`DeviceSession` is the authority for connection lifecycle, device information, command correlation, and stream state. The ViewModel displays that state and cannot directly write protocol bytes. A transport cannot interpret ACK/NACK, device state, or telemetry meaning.

## Connection generation

Every successful connection attempt obtains a monotonically increasing generation. Pending requests and received frames are associated with that generation. Disconnect retires the generation, cancels pending requests, closes the transport, and awaits the receive loop within the configured bound. A response from a prior generation cannot satisfy a new request.

## Bounded work

The specific capacities, overflow policies, and shutdown ownership are recorded in `Concurrency_Model.md`. Continuous acquisition never depends on WPF rendering or file I/O. UI overload discards oldest display samples and exposes the count; recorder overload exposes incomplete evidence rather than silently claiming success.

## Failure boundaries

- malformed external data is rejected before business use;
- expected cancellation is classified separately from failure;
- receive-loop failures fault the owning session;
- UI event boundaries observe exceptions;
- subscriber failures are isolated from protocol acquisition;
- incomplete shutdown is reported rather than hidden.

## Extension boundary

Additional transport implementations can be introduced behind `IDeviceTransport`. Multi-Node support must use independent Node/session contexts and a bounded registry; it must not overload the current single-session ViewModel with implicit routing.
