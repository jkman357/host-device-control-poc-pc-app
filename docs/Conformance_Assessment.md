# Conformance Assessment

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

## Candidate assessment

The 0.3.4 candidate implements the PC-side behavior of the external system protocol
v0.1.0 pinned in `protocol/authority-lock.yaml`. The local YAML is validated by exact
SHA-256, and derived C# message IDs, result codes, Node states, status flags, framing
constants, timeouts, stream limits, and normative vectors are checked for drift.

The implementation also retains the five adopted Coordinator/C# engineering documents
through bounded work, explicit ownership, generation-aware request correlation,
cancellation and timeout handling, WPF thread marshalling, visible overload/error
counters, fault-injection tests, and repository validation.

## Evidence already represented by this candidate

- static protocol authority/provenance validation;
- exact PING and ACK normative vector checks;
- PC encoder/decoder and payload codec coverage;
- idle/streaming state and ACK/NACK state handling;
- authoritative PING recovery after ambiguous START/STOP cancellation and timeout;
- apply-command-then-suppress-ACK fault injection for both state transitions;
- bounded Fake Node command/telemetry integration;
- partial-frame timeout, CRC rejection, sample-loss, command-timeout, and cancellation tests.

## Remaining acceptance gates

- clean controlled Windows restore/build and analyzer review;
- `dotnet format` verification and engineering test execution with retained output in the Actions job summary;
- manual WPF UI checklist;
- package-lock generation and locked restore;
- MCU implementation from the same authority commit;
- shared vector execution on both PC and MCU;
- sustained physical serial interoperability and disconnect/reconnect evidence;
- pinned PC and MCU implementation commits in the system repository;
- human protocol, architecture, code, evidence, and baseline approval.

Until all system-level gates pass, the repository must be described as a
**system-protocol-alignment candidate**, not a verified baseline or product implementation.
