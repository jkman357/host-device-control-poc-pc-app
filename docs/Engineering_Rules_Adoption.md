# Engineering Rules Adoption

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

## Adoption decision

This Project explicitly adopts the following documents as Project engineering authorities for this PoC implementation. The adoption is pinned to `host-device-control-framework` commit `7a68980ef5faa2e897a3574af121683d65f74638`.

| Document | Version | Upstream status | Project use |
|---|---:|---|---|
| `Coordinator_Software_Engineering_Rules.md` | v1.1.0 | Draft for Review | Coordinator-wide architecture and minimum engineering constraints |
| `CSharp_Coding_Rules.md` | v1.0.4 | Draft for Review | Product-owned C# implementation rules |
| `Coordinator_Concurrency_Guide.md` | v1.1.0 | Draft for Review | Detailed concurrency, cancellation, bounded-work, and shutdown rules |
| `Coordinator_UI_Engineering_Guide.md` | v1.1.0 | Draft for Review | Detailed WPF presentation, state, responsiveness, and feedback rules |
| `Coordinator_Testing_Guide.md` | v1.1.0 | Draft for Review | Test layering, determinism, fault injection, and evidence rules |

The upstream status remains **Draft for Review**. Project adoption does not approve, release, or alter those upstream documents. It means this Project intentionally uses them as review criteria, subject to the recorded Project profile and deviations.

## Authority order

1. External Project Protocol: `host-device-control-poc-system/protocol/protocol.yaml` at the commit pinned in `protocol/authority-lock.yaml`.
2. Protocol provenance lock and exact local mirror used for offline validation.
3. Project-specific approved decisions and profile in this repository.
4. Coordinator Software Engineering Rules.
5. Topic-specific Coordinator Guides for concurrency, UI, and testing.
6. C# Coding Rules for language and implementation details.
7. Tool defaults and informal conventions.

A lower authority cannot silently override a higher authority. Conflicts and justified departures must be recorded in `Project_Profile.md` before acceptance.

## Human responsibility

Automated validation detects selected structural inconsistencies only. Human engineers remain responsible for requirements, architecture approval, protocol approval, code review, hardware behavior, verification evidence, release decisions, and risk acceptance.
