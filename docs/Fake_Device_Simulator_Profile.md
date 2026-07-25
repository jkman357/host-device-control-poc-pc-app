# Fake Device Simulator Profile

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

The fake transport implements the same bounded byte-stream and binary protocol interface as the Serial transport. It supports device-information handshake, stream configuration, start/stop, 200 Hz sine telemetry, delayed or suppressed command responses, periodic sample loss, and periodic CRC corruption.

The simulator is deterministic enough for engineering tests but does not model USB/UART driver scheduling, partial writes from a real OS driver, electrical faults, MCU clock error, reset behavior, bootloader behavior, power loss, or safety behavior. Its results must be labelled as simulator evidence.
