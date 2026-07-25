<!-- Copyright © 2026 Ray Yang. All rights reserved. No license is granted. -->

# Protocol Test Vectors

`protocol-v0.1.0-vectors.json` is an exact mirror of the normative vector set from
the system protocol authority pinned in `../authority-lock.yaml`. This PC repository
does not independently own or redefine the vectors.

Both PC and MCU implementations must reproduce every byte, including CRC. A passing
round-trip test inside only one implementation is insufficient because the same defect
can exist in its encoder and decoder.

Hex strings contain no separators and are ordered exactly as transmitted on the wire.
Any vector change must first be approved and committed in `host-device-control-poc-system`,
then mirrored here with updated provenance and implementation evidence.
