# Testing Strategy

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

## Layers

1. Static repository validation: copyright headers, authority files, XML/XAML structure, protocol-to-C# identity checks, and prohibited unbounded-queue patterns.
2. Protocol unit tests: CRC, framing, fragmentation, noise recovery, unknown IDs, malformed payloads, and non-finite values.
3. Concurrency/policy tests: bounded drop-oldest behavior, command timeout, cancellation, and deterministic shutdown paths.
4. Simulator integration tests: handshake, stream start/stop, CRC corruption, and sample loss.
5. Manual WPF review: command-state enablement, status/error visibility, chart responsiveness, recording lifecycle, and close behavior.
6. Physical integration: shared protocol vectors, STM32 handshake, sustained 200 Hz telemetry, disconnect/reconnect, corruption, and timing evidence.

## Evidence rules

Each executable test run prints software candidate, base commit, protocol version, runtime, OS, and simulator identity. Simulator results are software evidence only and cannot substitute for physical transport, MCU timing, electrical, or safety validation. Failures must not be converted into passing evidence by retry without recording the original result and cause.
