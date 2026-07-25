# Shared PC/MCU Protocol Contract

`protocol.yaml` is the authoritative wire-level contract for both repositories:

- `host-device-control-poc-pc-app`
- `host-device-control-poc-stm32f446re-fw`

The protocol must not be treated as PC-owned or MCU-owned. A change is complete
only when the contract, test vectors, PC implementation, MCU implementation, and
bring-up evidence agree.

## Change procedure

1. Propose the message or field change in `protocol.yaml`.
2. Confirm direction, state preconditions, response, timeout, and error behavior.
3. Decide whether the wire version changes.
4. Update `test-vectors/protocol-v0.1.0-vectors.json` or create a new versioned set.
5. Update both implementations independently.
6. Compare the encoded bytes across languages.
7. Run hardware bring-up and retain evidence.

## Current status

Version `0x01` is a PoC baseline. It is sufficiently defined for parallel PC and
MCU development, but it is not a production or safety-approved protocol.
