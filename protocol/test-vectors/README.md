# Protocol Test Vectors

`protocol-v0.1.0-vectors.json` contains complete frames encoded according to
`protocol/protocol.yaml`.

Both PC and MCU implementations should load or manually reproduce these vectors
and compare every byte, including CRC. A passing round-trip test inside only one
implementation is insufficient because the same defect can exist in its encoder
and decoder.

Hex strings contain no separators and are ordered exactly as transmitted on the
wire.
