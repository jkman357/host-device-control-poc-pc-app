# Notice

Copyright © 2026 Ray Yang. All rights reserved.

This repository is a proof of concept for engineering evaluation. It is not a released
product, medical-device implementation, safety mechanism, validated test system,
production data-acquisition system, or certification of conformance.

The 0.3.9 candidate consumes protocol v0.1.0 from
`host-device-control-poc-system/protocol/protocol.yaml`, pinned by
`protocol/authority-lock.yaml`. The local protocol file is an offline mirror and does not
replace the system-level authority. The protocol remains `candidate_for_alignment` until
PC, MCU, shared-vector, hardware, pinned-commit, and human-approval gates are complete.

The selectable-baud transport profile remains a pending proposal and is not upstream
authority. The exact local protocol mirror must remain unchanged until an approved
system-repository commit establishes the new profile.

The candidate also records Project-local adoption of five engineering documents from
`host-device-control-framework`; those documents remain `Draft for Review`. Automated
checks cover selected structural and contract rules only.

Human engineers remain responsible for:

- defining and approving requirements;
- reviewing protocol authority, compatibility, architecture, code, deviations, and evidence;
- confirming electrical, driver, resource, timing, and failure assumptions;
- verifying real PC/MCU interoperability and failure handling;
- performing risk analysis and product verification;
- approving any protocol promotion or reuse in another system.

No open-source license is granted. See `LICENSE`.
