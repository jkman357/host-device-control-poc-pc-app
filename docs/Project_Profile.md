# Project Profile

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

## Identity and scope

- Repository: `host-device-control-poc-pc-app`
- Candidate version: 0.2.0
- Implementation base: `84bbc16f02a864084b1270db40b58460ad691e35`
- Role: single-Node Windows Coordinator PoC
- Device profile: NUCLEO-F446RE firmware PoC
- Protocol: `protocol/protocol.yaml` v0.1.0
- Deployment: engineering workstation; not production or clinical software
- Supported transports: bounded Fake byte stream and Windows SerialPort/VCP
- Storage model: operator-selected local CSV file plus bounded in-memory UI/log snapshots

## C# and platform profile

- Runtime target: .NET 8
- Language: C# 12
- UI: WPF on Windows
- IDE: Visual Studio with the .NET 8 and WPF workloads; command-line SDK is also supported
- Nullable reference types: enabled
- Implicit global usings: disabled
- Compiler/analyzer profile: .NET analyzers, `latest-recommended`, code style enforced during build
- Warning policy: warnings are visible in build/CI; warnings-as-errors is temporarily not enabled under `DEV-003`
- Formatting: repository `.editorconfig`; CI verifies `dotnet format`
- External package: `System.IO.Ports` 8.0.0, fixed direct version
- Package lock: not yet committed; see `DEV-004`
- Native dependencies: Windows serial stack, USB CDC/VCP driver, and physical device drivers when Serial mode is used
- Generated code: none
- Protocol code: manually derived enums/constants with automated cross-check against `protocol.yaml`
- Serialization: bounded binary protocol and CSV output; no polymorphic or untrusted object deserialization
- Test framework: dependency-free executable engineering test harness

## Architecture profile

- UI owns presentation state only.
- `DeviceSession` owns one connection generation, receive loop, command correlation, and protocol state.
- `IDeviceTransport` owns byte-stream I/O only.
- Serial and Fake transports are replaceable behind the same interface.
- One application instance supports one active Node session.
- Device identity and protocol state are never owned by WPF controls.

## Concurrency and overload policy

- One owned receive loop per connection generation.
- At most eight pending correlated requests.
- Transport receive buffers are bounded.
- UI telemetry buffer is bounded to 2,048 samples and drops oldest data under overload; drop count is visible.
- UI drains at most 512 samples every 50 ms.
- Recorder queue is bounded; overflow is surfaced and recording evidence is marked incomplete.
- Shutdown cancels application work and applies explicit time bounds.
- Background exceptions terminate or fault the owning operation; they are not intentionally abandoned.

## Security and trust profile

This PoC assumes a locally connected engineering device in a controlled environment. The protocol does not provide authentication, authorization, confidentiality, or anti-replay protection. Inputs are nevertheless length-bounded, enum-validated, CRC-checked, and strict-UTF-8 decoded. Logs sanitize control characters. This profile is not acceptable for an untrusted network or safety-related release without a separate security design.

## Deviation records and open controls

These records are **pending human approval** for the 0.2.0 candidate. They do not become accepted defaults merely by being documented.

### DEV-001 — SerialPort open/close cancellation

- Rule/section: Coordinator Concurrency Guide — asynchronous I/O, cancellation, owned work, bounded shutdown; C# Coding Rules — resource ownership and cancellation.
- Reason: `SerialPort.Open()` and `SerialPort.Close()` expose blocking platform calls without in-call cancellation.
- Scope: `SerialDeviceTransport.ConnectAsync` and `DisconnectAsync` only.
- Risk: a defective OS driver can delay connection or shutdown beyond the application expectation.
- Compensating control: run the calls on owned worker tasks, check cancellation before/after, cancel the session receive loop first, surface incomplete shutdown, and prohibit fire-and-forget calls.
- Verification: controlled target-driver connect/disconnect stress test and shutdown-time evidence.
- Approver: pending Ray Yang review.
- Review condition: close or revise after physical NUCLEO/VCP stress testing.

### DEV-002 — Protocol source generation

- Rule/section: Coordinator Software Engineering Rules — deterministic Protocol generation/validation; C# Coding Rules — external enum and Protocol authority.
- Reason: the PoC does not yet include a YAML-to-C# generator.
- Scope: `MessageType`, `ResultCode`, and `ProtocolConstants`.
- Risk: manual derived code can drift from the wire authority.
- Compensating control: `tools/validate_protocol_contract.py` compares identifiers and key constants with `protocol.yaml`; CI rejects drift; cross-language vectors remain mandatory.
- Verification: static validator plus PC/MCU vector execution.
- Approver: pending Ray Yang review.
- Review condition: replace with generation when the Protocol becomes broader or production-bound.

### DEV-003 — Warnings as errors

- Rule/section: C# Coding Rules — controlled analyzers, warning policy, and conformance build.
- Reason: the selected `latest-recommended` analyzer set has not yet been compiled in the controlled Windows environment.
- Scope: repository-wide build property `TreatWarningsAsErrors=false`.
- Risk: a new warning can remain non-blocking.
- Compensating control: analyzers and code-style checks are enabled; CI builds, verifies formatting, and requires human warning review before baseline acceptance.
- Verification: retained clean build/analyzer output.
- Approver: pending Ray Yang review.
- Review condition: enable warnings-as-errors after the first reviewed clean build.

### DEV-004 — NuGet lock file

- Rule/section: C# Coding Rules — dependency and controlled-build reproducibility.
- Reason: the current packaging environment does not include the .NET SDK needed to generate an authentic lock file.
- Scope: `System.IO.Ports` package restore.
- Risk: transitive dependency resolution is not yet immutably recorded.
- Compensating control: the direct version is fixed to 8.0.0; the first controlled restore must use `--use-lock-file`, review, and commit the generated file.
- Verification: locked restore and package inventory in CI.
- Approver: pending Ray Yang review.
- Review condition: must close before release-baseline designation.

### DEV-005 — UI automation coverage

- Rule/section: Coordinator UI Engineering Guide and Coordinator Testing Guide — UI behavior, accessibility, and deterministic verification.
- Reason: this PoC candidate does not yet include an installed WPF UI automation harness.
- Scope: direct control interaction, focus/navigation, and rendered accessibility verification.
- Risk: a binding, focus, or enablement regression may escape lower-layer tests.
- Compensating control: explicit one-/two-way bindings, command-state derivation, automation names, virtualized log, manual UI checklist, and extensive non-UI session/fault tests.
- Verification: retained manual checklist until automation is added.
- Approver: pending Ray Yang review.
- Review condition: automation required before product-grade UI baseline.

## Conformance statement

This repository is an **engineering-rules-aligned candidate**, not a certification or unconditional conformance claim. Acceptance requires a clean controlled Windows build, analyzer review, test execution, manual UI review, protocol cross-end validation, physical MCU validation, and human approval.
