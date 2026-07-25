# Host-Device Control PoC PC App

**Copyright © 2026 Ray Yang. All rights reserved. No license is granted.**

Windows WPF Coordinator application for the NUCLEO-F446RE host-device-control proof of concept.

## Status

**v0.2.3 engineering-rules-aligned candidate** based on repository commit `84bbc16f02a864084b1270db40b58460ad691e35`.

This revision explicitly adopts five engineering documents from `host-device-control-framework` commit `7a68980ef5faa2e897a3574af121683d65f74638`:

- Coordinator Software Engineering Rules v1.1.0
- C# Coding Rules v1.0.4
- Coordinator Concurrency Guide v1.1.0
- Coordinator UI Engineering Guide v1.1.0
- Coordinator Testing Guide v1.1.0

All five upstream documents remain **Draft for Review**. This repository records Project-local adoption and deviations; it does not promote their upstream status or claim unconditional conformance. See `docs/Engineering_Rules_Adoption.md`, `docs/Project_Profile.md`, and `docs/Conformance_Assessment.md`.

## What this candidate demonstrates

- transport-independent `DeviceSession`;
- framed binary protocol with CRC-16/CCITT-FALSE;
- validated external IDs, payload lengths, UTF-8, and finite telemetry;
- generation-aware command correlation, cancellation, and timeouts;
- bounded receive, request, UI, and recorder work;
- 200 Hz acquisition with a bounded 50 ms WPF presentation batch;
- visible CRC, format, unknown-ID, sample-loss, UI-drop, and recorder-drop counters;
- CSV recording with explicit overrun reporting;
- fake-device fault injection for CRC corruption, sample loss, timeout, and cancellation;
- static Project Protocol-to-C# drift checking;
- source-file copyright headers and repository-level no-license notice.

## Authoritative communication contract

```text
protocol/protocol.yaml
```

The PC and MCU implementations must use the same protocol version and test vectors. Protocol code is currently derived manually and checked by `tools/validate_protocol_contract.py`.

## Prerequisites

- Windows 10 or Windows 11
- .NET 8 or a later stable .NET SDK capable of targeting `net8.0-windows`
- Visual Studio with the `.NET desktop development` workload when using the IDE
- Python 3 for repository validators

The application targets .NET 8. Local SDK selection intentionally uses the latest stable SDK installed on the workstation so Visual Studio 2026 systems are not blocked when the exact .NET 8 SDK feature band is absent. GitHub Actions still installs and builds with `8.0.x` as the controlled CI baseline.

## Validate, build, and test

```powershell
./scripts/validate.ps1
./scripts/build.ps1
./scripts/test.ps1
```

The first controlled environment should also generate and commit the NuGet lock file:

```powershell
dotnet restore HostDeviceControl.Poc.sln --use-lock-file
```

After that baseline is reviewed, CI should be changed to locked restore.

## Run from Visual Studio

The executable project is `HostDeviceControl.App`. Set it as the startup project, then press `F5` or `Ctrl+F5`. A shared `HostDeviceControl.Poc.slnLaunch` profile is included for Visual Studio versions that support it.

### SDK resolution troubleshooting

Check the SDKs visible to Visual Studio and the command line:

```powershell
dotnet --list-sdks
dotnet --info
```

`global.json` does not pin an unavailable feature band. It selects the latest installed stable SDK and keeps the target framework at `net8.0-windows`. If no SDK is listed, install the `.NET desktop development` workload through Visual Studio Installer.

## Run without hardware

```powershell
./scripts/run-fake.ps1
```

Then select `Fake Device`, connect, and start the stream. The fake path still traverses binary framing, decoding, command correlation, session state, bounded telemetry delivery, and WPF rendering.

## Run with NUCLEO-F446RE

1. Connect the board through ST-LINK USB.
2. Confirm the Virtual COM Port in Windows Device Manager.
3. Select `Serial Port`, the COM port, and 115200 baud.
4. Connect and start streaming.
5. Retain output from the cross-end protocol and sustained-stream checks in `docs/Bringup_Checklist.md`.

## Repository structure

```text
src/
  HostDeviceControl.App/              WPF UI and composition
  HostDeviceControl.Core/             protocol, session, models, concurrency
  HostDeviceControl.Transport.Fake/   bounded simulator and fault injection
  HostDeviceControl.Transport.Serial/ Windows serial adapter
tests/
  HostDeviceControl.Protocol.Tests/   executable engineering tests
tools/
  validate_project.py                 repository validator entry point
  validate_protocol_contract.py       YAML/C# identity cross-check
  validate_source_headers.py          copyright and bounded-work checks
protocol/
  protocol.yaml                       authoritative PC/MCU contract
  test-vectors/                       cross-language examples
docs/
  Engineering_Rules_Adoption.md
  Project_Profile.md
  Concurrency_Model.md
  UI_Engineering_Profile.md
  Testing_Strategy.md
  Fake_Device_Simulator_Profile.md
  Conformance_Assessment.md
```

## Responsibility boundaries

- `App` does not parse transport bytes or calculate CRC.
- `Core` does not depend on WPF or `SerialPort`.
- transports move bounded byte streams and do not own UI state.
- `DeviceSession` owns connection-generation protocol state.
- the UI displays authoritative session state and does not invent device state.
- fake-device evidence is not physical-device evidence.

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
