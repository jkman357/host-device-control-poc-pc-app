<!-- Copyright © 2026 Ray Yang. All rights reserved. No license is granted. -->

# Protocol Implementation Alignment

## Authority boundary

The project-level protocol authority is
`host-device-control-poc-system/protocol/protocol.yaml`, pinned at authority commit
`e4aa40b4d5dfc3e7f878f82f5a89115de9fe3679`. This repository is an implementation
consumer. Its local YAML and vectors are controlled mirrors used for offline build
and drift validation; they do not override the system repository.

## Implemented v0.1.0 behavior

- binary frame `A5 5A | version | uint8 message_id | uint16 sequence |
  uint16 payload_length | payload | uint16 CRC`;
- little-endian unsigned fields, IEEE-754 binary32 telemetry, and little-endian CRC;
- CRC-16/CCITT-FALSE over version through end of payload;
- non-zero monotonically increasing host command sequence with `0xFFFF -> 1` wrap;
- direct responses copy the request sequence;
- ACK and NACK carry request message ID, result code, and device state;
- device states are `idle` and `streaming`;
- `SET_STREAM_CONFIG` and `START_STREAM` are idle-only; `STOP_STREAM` is streaming-only;
- telemetry is accepted only while the reported device state is streaming;
- DEVICE_STATUS and ERROR_REPORT payloads are decoded as unsolicited messages;
- duplicate direct responses are ignored after the first matching response;
- unmatched response sequences are rejected as unmatched diagnostics;
- partial frames are discarded after 250 ms;
- command timeouts are 1000 ms, except STOP_STREAM at 1500 ms;
- the fake Node returns INVALID_COMMAND for a decodable unknown request ID and
  implements the same state transition and ACK/NACK payload rules.

## Evidence boundary

The C# encoder, parser, state handling, fake Node, and normative vector checks can
establish PC implementation alignment. Hardware interoperability, MCU alignment,
and promotion to `verified_baseline` remain external gates owned by the system
repository and human approval.
