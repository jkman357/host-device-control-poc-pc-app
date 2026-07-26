# System Transport-Profile Change Proposal

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

## Purpose

The pinned system protocol currently defines one fixed UART rate of 115200 baud. The
PC candidate exposes eleven requested rates, so the system repository must approve and
own the transport-profile change before non-115200 operation can be called aligned.

## Proposed authority change

Replace the fixed `baud_rate_bps` field in
`host-device-control-poc-system/protocol/protocol.yaml` with an explicit allowed set and
default:

```yaml
transport_profile:
  name: st_link_vcp_uart
  physical_path: st_link_virtual_com_port
  allowed_baud_rates_bps:
    - 1200
    - 2400
    - 4800
    - 9600
    - 19200
    - 38400
    - 57600
    - 115200
    - 230400
    - 460800
    - 921600
  default_baud_rate_bps: 115200
  data_bits: 8
  parity: none
  stop_bits: 1
  flow_control: none
  duplex: full_duplex
```

Add a capacity rule or an equivalent normative decision:

```yaml
stream_capacity_policy:
  bits_per_uart_byte: 10
  maximum_line_utilization_percent: 80
  behavior: auto_increase_interval_or_reject_streaming
```

## Compatibility classification

The frame format, message IDs, payload layouts, CRC, and wire version do not change.
The change affects physical transport configuration and achievable telemetry rate.
Non-115200 use is incompatible with an MCU build that remains fixed at 115200.

## Required coordinated updates

1. Human approval and commit in `host-device-control-poc-system`.
2. Refresh the PC and MCU protocol mirrors, authority locks, and hashes.
3. Implement the same allowed/default rates, fixed 8-N-1/no-flow-control framing, and capacity policy in MCU configuration.
4. Confirm UART divisor error and clock tolerance at every promoted rate.
5. Run shared vectors plus sustained physical stream, stop, reconnect, CRC, and loss
   tests at every promoted rate.
6. Pin the accepted PC and MCU commits in the system repository before baseline
   promotion.
