<!-- Copyright © 2026 Ray Yang. All rights reserved. No license is granted. -->

# Project Protocol Mirror

The wire protocol is **not owned by this PC repository**. The project-level authority is:

```text
host-device-control-poc-system/protocol/protocol.yaml
```

The local `protocol/protocol.yaml` is a byte-for-byte mirror of authority commit
`e4aa40b4d5dfc3e7f878f82f5a89115de9fe3679`, SHA-256
`7ff8db3a1ed669407e0d4cada2a78b212ea3c7bccdf371f232a2689a02e7c56e`.
The provenance record is `authority-lock.yaml`.

Do not independently edit the local mirror. Change the system repository first,
classify compatibility and wire-version impact, commit the authority, then update
this mirror, lock, derived C# implementation, normative vectors, and evidence.

Protocol v0.1.0 / wire version 0x01 remains `candidate_for_alignment`. A successful
PC build or fake-device run does not promote it to `verified_baseline`.

## Pending serial transport-profile change

`transport-profile-proposal.yaml` records the requested selectable baud-rate set and
capacity policy. Its controlled SHA-256 is
`6d7d62f88f0b7b62e6a1468dba0ae9797447a1e295848f4bce8d5eda21645310`. It is deliberately separate from the exact pinned protocol mirror.
The proposal does not become system authority until a human-approved change is
committed to `host-device-control-poc-system`, after which the mirror, lock, PC, MCU,
and evidence must be updated together. The current upstream default remains 115200.
