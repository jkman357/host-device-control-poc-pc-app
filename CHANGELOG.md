# Changelog

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

## 0.3.3 - 2026-07-26

- restored complete protocol-contract validation for identifiers, status flags, framing, payload sizes, stream limits, timeouts, authority provenance, and normative vectors;
- recovered authoritative Node state through an independently bounded PING after START_STREAM or STOP_STREAM cancellation and timeout;
- synchronized public PING results into the session Ready/Streaming state;
- added fault injection that applies a command and then suppresses its ACK, covering indeterminate command outcomes rather than only pre-execution cancellation;
- added cancellation and timeout recovery regression tests for both start and stop operations;
- recovered authoritative state after malformed or mismatched state-command responses before rethrowing the protocol error;
- retained complete protocol-test exception details in the GitHub Actions step summary;
- completed the WPF asynchronous disposal pattern with idempotence and `GC.SuppressFinalize`;
- updated candidate provenance from implementation commit `ba74d943e87deb8e51771e6a397b1b07fe37c8ed`.

## 0.3.2 - 2026-07-25

- fixed CI naming-policy overlap between private instance fields, constants, and static readonly fields;
- updated GitHub Actions to Node.js 24-compatible major versions;
- validated ACK and NACK request IDs before applying authoritative device state;
- synchronized reconnect state with a PING handshake instead of assuming Idle;
- preserved Ready/Streaming state for caller cancellation instead of forcing Faulted;
- bounded session, recorder, and serial-transport disposal waits;
- added regression tests for response correlation, reconnect state, and cancellation recovery.

## 0.3.1 — Build portability and analyzer cleanup

- Added the missing `System.Threading.Tasks` import required by `ValueTask` in `MainWindow.DisposeAsync()`.
- Renamed the public flags enum from `StatusFlags` to `DeviceStatusBits` to satisfy CA1711 while preserving the wire field `status_flags`.
- Normalized repository text files to LF and added `.gitattributes` to keep Git checkouts consistent across Windows and CI.
- Added static line-ending validation so CRLF drift is detected before `dotnet format`.
- Kept the shared System Protocol wire contract and normative vectors unchanged.

## 0.3.0 — System protocol authority alignment

- Rebased implementation work on PC application commit `432d0f5863698bb7d5ed2ad337d02f690f4175b8`.
- Replaced the locally owned protocol copy with an exact mirror of `host-device-control-poc-system/protocol/protocol.yaml` authority commit `e4aa40b4d5dfc3e7f878f82f5a89115de9fe3679`.
- Added protocol provenance locking and exact SHA-256 validation.
- Updated ACK/NACK to the authoritative three-byte payload: request ID, result code, and Node state.
- Added idle/streaming Node-state tracking, transition validation, and state-aware command handling.
- Added DEVICE_STATUS and ERROR_REPORT codecs, events, diagnostics, and UI state summary integration.
- Added protocol-defined command and partial-frame timeouts, duplicate-response suppression, and unmatched-response diagnostics.
- Updated the Fake Node to enforce payload lengths, protocol version, command states, state transitions, and authoritative ACK/NACK behavior.
- Mirrored and validated the system normative PING and ACK vectors.
- Added tests for ACK vector alignment, Node state, partial-frame discard, DEVICE_STATUS, and ERROR_REPORT.
- Resolved the previously reported analyzer findings for argument validation, concrete brush return type, and WPF async disposal ownership.

## 0.2.3 — Cross-platform source-policy validation fix

- Normalized repository-relative paths with `Path.as_posix()` before comparing the approved `async void` boundary allowlist.
- Fixed the Windows GitHub Actions false positive for `MainWindow.xaml.cs`.
- Kept the approved WPF shutdown event boundary and all runtime behavior unchanged.

## 0.2.2 — Core compilation and explicit interface-accessibility fix

- Added the missing `System.Threading` import required by `Timeout.InfiniteTimeSpan`.
- Added explicit `public` modifiers to `IDeviceTransport` members to satisfy the adopted accessibility policy and remove IDE0040 warnings.
- No wire-protocol or runtime-behavior change.

## 0.2.1 — Visual Studio 2026 SDK-resolution fix

- Removed the exact `.NET SDK 8.0.100` feature-band requirement that could trigger `NETSDK1141` on Visual Studio 2026 systems.
- Changed `global.json` to select the latest installed stable SDK while retaining the `net8.0-windows` target framework.
- Kept GitHub Actions on the controlled `8.0.x` SDK baseline.
- Documented local/CI SDK selection, verification commands, and the associated controlled deviation.

## 0.2.0 — Engineering-rules-aligned candidate

- Explicitly adopted the five pinned Coordinator/C# engineering authorities.
- Added Project profile, deviations, concurrency model, UI profile, testing strategy, simulator profile, and conformance assessment.
- Replaced unbounded UI telemetry accumulation with a bounded drop-oldest buffer and visible drop count.
- Added bounded request correlation, generation ownership, cancellation, shutdown timeouts, and safer background-task ownership.
- Added external message/result validation, strict UTF-8, finite telemetry validation, and bounded decoder behavior.
- Added recorder-overrun reporting and bounded CSV writer work.
- Added fake-device CRC, sample-loss, timeout, and cancellation fault injection.
- Added expanded executable tests and protocol-contract/static repository validators.
- Added application and framework event exception boundaries.
- Added source-file copyright headers and assembly copyright metadata.

## 0.1.2 — PoC baseline

- Corrected WPF display bindings to use explicit one-way mode.
- Retained Fake and Serial transport paths, telemetry chart, and recording.
