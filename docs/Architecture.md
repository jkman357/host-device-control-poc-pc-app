# PC Application Architecture

## 1. Purpose

This document defines the PC application responsibility boundaries for the
NUCLEO-F446RE host-device-control proof of concept.

## 2. Runtime data flow

```text
SerialDeviceTransport or FakeDeviceTransport
    -> DeviceSession receive loop
    -> FrameDecoder
    -> command response correlation or telemetry decode
    -> telemetry queue
    -> 50 ms UI batching and optional CSV recorder
    -> WPF waveform and counters
```

## 3. Project responsibilities

### HostDeviceControl.App

Owns WPF views, view models, UI commands, waveform presentation, port selection,
and CSV-recording interaction. It must not implement protocol framing, CRC, or
serial receive parsing.

### HostDeviceControl.Core

Owns transport abstractions, protocol framing, payload codecs, command/response
correlation, timeout handling, device-session state, telemetry models, and
protocol statistics. It has no WPF or serial-port dependency.

### HostDeviceControl.Transport.Serial

Owns COM-port discovery and byte-stream I/O. It must not interpret message IDs,
payloads, ACK/NACK semantics, or telemetry.

### HostDeviceControl.Transport.Fake

Implements the same byte-stream interface as serial transport and simulates the
MCU protocol. It supports PC development, demos, and regression testing before
hardware or firmware is available.

## 4. Concurrency model

- exactly one receive loop reads a transport;
- the frame decoder is owned by that receive loop;
- pending commands are correlated by sequence number;
- continuations run asynchronously to avoid receive-loop reentrancy;
- telemetry callbacks never update WPF collections directly;
- the view model enqueues telemetry from the receive thread;
- a WPF dispatcher timer drains the queue every 50 ms;
- CSV recording uses a separate bounded channel and writer task;
- UI or file I/O must never block the receive loop.

## 5. Device-session states

```text
Disconnected
  -> Connecting
  -> Handshaking
  -> Ready
  -> StartingStream
  -> Streaming
  -> StoppingStream
  -> Ready
  -> Disconnecting
  -> Disconnected
```

Any unrecoverable transport or protocol failure may move the session to
`Faulted`. The current PoC requires an explicit disconnect and reconnect after a
fault.

## 6. Buffer policy

### UI path

The live chart retains only the newest configured sample window. Older chart
points may be discarded because display freshness is more important than UI
history completeness.

### Recording path

The recorder uses a bounded queue. A queue overrun is counted and reported; it
is never silently treated as complete data. The receive loop remains
non-blocking even if the storage device is slow.

## 7. Extension points

The design can add TCP, native USB, replay-file, or automated-test transports
without changing `DeviceSession`. Multi-device support should be introduced by
creating multiple independent session objects, not by adding device routing to
the existing single-session view model.
