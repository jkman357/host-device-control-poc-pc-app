# Fake Device Simulator Profile

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

The Fake transport implements a bounded byte-stream Node simulator behind the same
`IDeviceTransport` interface as the Windows Serial transport. Its protocol behavior is
derived from the external system authority pinned in `protocol/authority-lock.yaml`.

Implemented behavior includes:

- PING and GET_DEVICE_INFO in idle or streaming state;
- SET_STREAM_CONFIG and START_STREAM only in idle state;
- STOP_STREAM only in streaming state;
- strict empty/two-byte command payload length checks;
- stream interval range validation;
- three-byte ACK/NACK payloads carrying authoritative Node state;
- unsupported-version and unknown-command NACK responses when the request is decodable;
- stream-counter reset on successful START_STREAM;
- 200 Hz default sine telemetry with independent frame sequence and uint32 sample counter;
- delayed/suppressed responses, periodic sample loss, and periodic CRC corruption.

The simulator is deterministic enough for PC engineering tests but does not model
USB/UART driver scheduling, physical partial writes, electrical faults, MCU clock error,
reset/boot behavior, power loss, or safety behavior. Its results must be labelled as
simulator evidence and cannot promote the system protocol to `verified_baseline`.
