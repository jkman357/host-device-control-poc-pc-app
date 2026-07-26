# Host-Device Control PoC PC App

**Copyright © 2026 Ray Yang. All rights reserved. No license is granted.**

Windows WPF Coordinator application for the NUCLEO-F446RE host-device-control proof of concept.

## Status

**v0.3.3 authoritative-state recovery candidate**, based on PC application commit
`ba74d943e87deb8e51771e6a397b1b07fe37c8ed`.

The wire contract is owned by `host-device-control-poc-system`, not by this repository:

```text
host-device-control-poc-system/protocol/protocol.yaml
```

This candidate is aligned to protocol authority commit
`e4aa40b4d5dfc3e7f878f82f5a89115de9fe3679`, reviewed through system commit
`e418afc3b3e866039c583a8ba4dc1a1049a9cec1`. The exact local mirror SHA-256 is
`7ff8db3a1ed669407e0d4cada2a78b212ea3c7bccdf371f232a2689a02e7c56e`.
See `protocol/authority-lock.yaml`.

Protocol v0.1.0 / wire version 0x01 remains `candidate_for_alignment`. A successful PC
build or Fake Device run is implementation evidence only and does not promote the
system protocol to `verified_baseline`.

## Implemented system protocol behavior

- exact `A5 5A` binary framing and CRC-16/CCITT-FALSE;
- little-endian integers, CRC storage, and IEEE-754 binary32 telemetry;
- PING, GET_DEVICE_INFO, SET_STREAM_CONFIG, START_STREAM, and STOP_STREAM;
- ACK/NACK payloads containing request ID, result code, and authoritative Node state;
- idle/streaming Node state model with command-state enforcement;
- DEVICE_INFO, DEVICE_STATUS, TELEMETRY_SAMPLE, and ERROR_REPORT decoding;
- non-zero host command sequences and independent unsolicited message sequences;
- validated ACK/NACK request correlation before authoritative Node-state updates;
- connection handshake and explicit PING state synchronization;
- independently bounded authoritative-state recovery after ambiguous START/STOP cancellation or timeout;
- duplicate direct-response suppression and unmatched-sequence diagnostics;
- 1000 ms default command timeout, 1500 ms STOP_STREAM timeout, and 250 ms partial-frame timeout;
- strict payload length, enum, UTF-8, finite-float, and stream-range validation;
- Fake Node INVALID_COMMAND, INVALID_LENGTH, INVALID_VALUE, INVALID_STATE, and UNSUPPORTED_VERSION behavior;
- normative cross-language PING and ACK frame vectors from the system repository.

## Engineering-rules baseline

The implementation continues to adopt these pinned documents from
`host-device-control-framework` commit `7a68980ef5faa2e897a3574af121683d65f74638`:

- Coordinator Software Engineering Rules v1.1.0
- C# Coding Rules v1.0.4
- Coordinator Concurrency Guide v1.1.0
- Coordinator UI Engineering Guide v1.1.0
- Coordinator Testing Guide v1.1.0

All remain **Draft for Review**. Project-local adoption and deviations are recorded in
`docs/Engineering_Rules_Adoption.md`, `docs/Project_Profile.md`, and
`docs/Conformance_Assessment.md`.

## Architecture highlights

- transport-independent, generation-owned `DeviceSession`;
- protocol and Node state separated from WPF presentation state;
- bounded receive, pending-command, UI telemetry, diagnostic, and recorder work;
- 200 Hz acquisition with a bounded 50 ms WPF presentation batch;
- visible CRC, format, unknown-ID, partial-frame, sample-loss, UI-drop, and recorder-drop counters;
- bounded Fake Device with CRC corruption, sample loss, command delay, response suppression, timeout, and cancellation injection;
- exact authority-mirror hashing and YAML-to-C# identity validation.

## Prerequisites

- Windows 10 or Windows 11
- .NET 8 or a later stable SDK capable of targeting `net8.0-windows`
- Visual Studio with the `.NET desktop development` workload
- Python 3 for repository validators

Local SDK selection uses the latest installed stable SDK. GitHub Actions installs
`8.0.x` as the controlled CI baseline.

## Validate, build, and test

```powershell
./scripts/validate.ps1
./scripts/build.ps1
./scripts/test.ps1
```

The first controlled environment should also generate and review a NuGet lock file:

```powershell
dotnet restore HostDeviceControl.Poc.sln --use-lock-file
```

## Run from Visual Studio

Set `HostDeviceControl.App` as the startup project, then press `F5` or `Ctrl+F5`.
A shared `HostDeviceControl.Poc.slnLaunch` profile is included.

## Run without hardware

```powershell
./scripts/run-fake.ps1
```

Select `Fake Device`, connect, and start the stream. The fake path still traverses
binary framing, protocol decoding, command correlation, Node state, bounded telemetry
delivery, and WPF rendering.

## Run with NUCLEO-F446RE

1. Build MCU firmware from the same system protocol authority commit.
2. Connect the board through ST-LINK USB and identify the Virtual COM Port.
3. Select `Serial Port`, the COM port, and 115200 baud.
4. Connect, verify DEVICE_INFO, configure 5000 us streaming, and start telemetry.
5. Execute and retain the checks in `docs/Bringup_Checklist.md`.

## Repository structure

```text
src/
  HostDeviceControl.App/              WPF UI and composition
  HostDeviceControl.Core/             protocol, session, models, concurrency
  HostDeviceControl.Transport.Fake/   bounded Node simulator and fault injection
  HostDeviceControl.Transport.Serial/ Windows serial adapter
tests/
  HostDeviceControl.Protocol.Tests/   executable engineering tests
tools/
  validate_project.py                 repository validator entry point
  validate_protocol_contract.py       authority hash/YAML/C#/vector checks
  validate_source_headers.py          ownership and bounded-work checks
protocol/
  authority-lock.yaml                 pinned external authority provenance
  protocol.yaml                       exact offline mirror; not local authority
  test-vectors/                       exact system normative vectors
docs/
  Protocol_Decisions.md
  Project_Profile.md
  Architecture.md
  Concurrency_Model.md
  UI_Engineering_Profile.md
  Testing_Strategy.md
  Bringup_Checklist.md
```

## Responsibility boundaries

- the system repository owns the wire contract;
- this repository consumes and validates an exact protocol mirror;
- `App` does not parse bytes or calculate CRC;
- `Core` does not depend on WPF or `SerialPort`;
- transports move bounded byte streams and do not own UI state;
- `DeviceSession` owns connection generation, request correlation, and observed Node state;
- Fake Device evidence is not physical-device evidence.

## Known limitations

- one active Node session;
- no automatic reconnect;
- exact wire version only;
- protocol security is not provided;
- no package lock file until first controlled restore;
- no automated WPF interaction tests;
- no installer or code signing;
- no claim of safety, clinical, regulatory, or production suitability.

## License and use

No open-source license or other permission is granted. See `LICENSE` and `NOTICE.md`.
