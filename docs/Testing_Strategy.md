# Testing Strategy

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

## Layers

1. Authority validation: exact protocol SHA-256, provenance lock, external protocol metadata, and exact system test-vector mirrors.
2. Static implementation validation: C# message IDs, result codes, Node states, status flags, framing constants, timeouts, stream bounds, ownership headers, and bounded-work patterns.
3. Protocol and transport-configuration tests: the portable protocol suite covers CRC, framing, vectors, parsing, state, timeout, and Fake Node behavior; the Windows serial suite covers the exact proposed baud-rate ordering, default, rejection, and capacity-derived intervals.
4. Concurrency/policy tests: bounded drop-oldest behavior, request timeout, cancellation, duplicate-response policy, authoritative PING recovery, and deterministic shutdown paths.
5. Simulator integration tests: handshake, idle/streaming transitions, stream start/stop, CRC corruption, sample loss, command failure behavior, and apply-command-then-suppress-ACK fault injection.
6. Manual WPF review: command-state enablement, Node-state/status/error visibility, chart responsiveness, recording lifecycle, and close behavior.
7. Physical integration: same authority commit and vectors on MCU, handshake, sustained 200 Hz telemetry, disconnect/reconnect, corruption/timing evidence, and pinned implementation commits.

## Evidence rules

Each executable test run prints software candidate, PC base commit, system protocol
authority, runtime, OS, and simulator identity. Simulator results are software evidence
only and cannot substitute for physical transport, MCU timing, electrical, or safety
validation. Failures must remain visible in retained evidence and cannot be converted to
passing evidence by undocumented retry. The GitHub Actions job summary retains each test name, exception, and stack trace when the executable harness returns failure.

## Repository-validator portability

Repository-relative paths used by policy allowlists are normalized to POSIX-style
separators before comparison so Windows and POSIX runners produce the same result.


## Serial transport and capacity tests

Windows-specific serial option and capacity tests are isolated in
`HostDeviceControl.Transport.Serial.Tests` so protocol/core tests remain `net8.0` and
portable. The serial suite verifies the exact proposed baud-rate set, the 115200
default, unsupported-rate rejection, command-only low rates, and every calculated
safe interval. CI runs both executable suites and retains both outputs in one evidence
artifact.
