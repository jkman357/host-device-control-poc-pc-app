# Changelog

## 0.2.2 — Core compilation and explicit interface-accessibility fix

- Added the missing `System.Threading` import required by `Timeout.InfiniteTimeSpan`.
- Added explicit `public` modifiers to `IDeviceTransport` members to satisfy the adopted accessibility policy and remove IDE0040 warnings.
- No wire-protocol or runtime-behavior change.

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

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
