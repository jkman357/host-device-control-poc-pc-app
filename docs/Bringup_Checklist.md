# PC/MCU Bring-up Checklist

## Before connecting hardware

- [ ] PC protocol self-test passes.
- [ ] MCU CRC function passes the `123456789 -> 0x29B1` vector.
- [ ] MCU encoder output matches `protocol/test-vectors` byte for byte.
- [ ] Both sides use little-endian fields.
- [ ] Both sides use protocol version `0x01`.
- [ ] UART is configured for 115200, 8 data bits, no parity, 1 stop bit.

## First connection

- [ ] Open the COM port without starting telemetry.
- [ ] Send `GET_DEVICE_INFO`.
- [ ] Confirm response sequence equals request sequence.
- [ ] Confirm device type and firmware version decode correctly.
- [ ] Confirm no CRC or parser errors.

## Streaming

- [ ] Send `SET_STREAM_CONFIG` with 5000 microseconds.
- [ ] Receive ACK.
- [ ] Send `START_STREAM`.
- [ ] Receive ACK before or at the defined start boundary.
- [ ] Confirm sample counter increments by one.
- [ ] Confirm device tick increments by approximately 5000 microseconds.
- [ ] Confirm 200 samples/second over at least 10 minutes.
- [ ] Confirm `STOP_STREAM` produces no further telemetry after the allowed drain.

## Error injection

- [ ] Corrupt one payload byte and confirm CRC rejection.
- [ ] Split a frame across multiple UART reads.
- [ ] concatenate multiple frames in one UART read.
- [ ] Insert garbage bytes before SOF and confirm resynchronization.
- [ ] Reset the MCU while connected and record expected host behavior.
- [ ] Unplug USB during streaming and confirm the host exits to a faulted state.

## Evidence to retain

- [ ] firmware commit;
- [ ] PC application commit;
- [ ] protocol version;
- [ ] test date and operator;
- [ ] COM port and baud rate;
- [ ] captured raw frames or logic-analyzer evidence;
- [ ] observed loss, CRC, and timeout counters;
- [ ] known deviations and owner.
