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

`DeviceSession` is the authority for PC connection lifecycle and request correlation, and it tracks the authoritative Node operating state reported by ACK/NACK and DEVICE_STATUS. After an ambiguous START_STREAM or STOP_STREAM cancellation, timeout, or malformed response, it performs an independently bounded PING before deciding whether the session is Ready, Streaming, or Faulted. The ViewModel displays that state and cannot directly write protocol bytes. A transport cannot interpret ACK/NACK, Node state, or telemetry meaning. The wire contract remains external to this repository and is consumed through the pinned protocol mirror.

`SerialTransportOptions` owns the exact supported baud-rate set and validates every Serial transport instance. Its public construction boundary accepts only port name and baud rate; data bits, parity, stop bits, and handshake are fixed to 8-N-1 with no flow control. The ViewModel exposes the same read-only baud set to the non-editable WPF selector, so presentation and transport validation cannot drift. The selected rate is captured before opening the port and cannot be changed while a session is active. `SerialStreamCapacity` applies the pending transport proposal: it models the enforced 8-N-1 profile as 10 bits per byte, reserves 20% of the line, keeps the 5000 us preference when safe, increases the interval at lower rates, and rejects streaming when the result would exceed the protocol maximum interval.

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
