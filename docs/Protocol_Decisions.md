# Protocol Decisions

## Contract-first development

The protocol is defined before either endpoint is complete. The PC fake device,
PC client, and MCU firmware are independent implementations of the same
contract. Neither endpoint is the protocol authority.

The authority is `protocol/protocol.yaml`.

## Why the protocol is defined now

Waiting for MCU code would create several avoidable risks:

- the PC application would become coupled to incidental MCU implementation;
- command, response, timeout, and error semantics would be decided late;
- the two teams could not work in parallel;
- test vectors would be unavailable during bring-up;
- framing defects would be discovered only with hardware connected.

## Baseline choices

- binary framing instead of CSV text;
- fixed two-byte start-of-frame marker;
- little-endian multi-byte fields to match STM32 and Windows hosts;
- explicit protocol version;
- 16-bit sequence number for request/response correlation;
- 16-bit payload length with a project limit of 1024 bytes;
- CRC-16/CCITT-FALSE over header fields after SOF plus payload;
- device-generated sample counter and device tick in telemetry;
- host receive time added locally and never transmitted as device time.

## Versioning rule

A receiver shall reject unsupported protocol versions. Backward-compatible
payload extensions are not assumed in version `0x01`; changing an existing
payload layout requires a protocol-version decision and updated test vectors.

## Sequence rule

- host commands use a non-zero sequence number;
- direct responses copy the command sequence number;
- unsolicited telemetry uses an independent device frame sequence;
- the telemetry payload includes a 32-bit sample counter for loss detection;
- sequence wraparound is valid and must not be treated as a reset by itself.

## Timing rule

The PoC baseline uses a 5000 microsecond stream interval. The protocol permits a
configurable interval from 1000 to 60000 microseconds, subject to UART bandwidth,
MCU timing, and actual validation.

## Items still requiring human confirmation

- final SOF value and whether byte stuffing is required for future payloads;
- exact device type identifier;
- required stream-rate range;
- timeout values after real NUCLEO measurements;
- whether device status becomes periodic or event-driven;
- protocol behavior across MCU reset while the COM port remains open;
- production needs for authentication, encryption, or stronger integrity.
