# Changelog

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

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
