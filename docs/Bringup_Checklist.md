<!-- Copyright © 2026 Ray Yang. All rights reserved. No license is granted. -->

# Physical Bring-up and UI Review Checklist

## Controlled build identity

- [ ] Record repository commit and dirty/clean state.
- [ ] Record .NET SDK, Visual Studio, Windows, and `System.IO.Ports` package versions.
- [ ] Run `scripts/validate.ps1`, `scripts/build.ps1`, and `scripts/test.ps1` and retain output.
- [ ] Review all compiler/analyzer warnings.
- [ ] Generate/review the NuGet lock file when first establishing the controlled baseline.

## WPF behavior

- [ ] `HostDeviceControl.App` is the startup project and launches without binding exceptions.
- [ ] Transport, COM port, and baud controls are disabled while connected.
- [ ] Baud selector contains exactly 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, and 921600; default selection is 115200.
- [ ] Button enablement follows Disconnected/Ready/Streaming states.
- [ ] Error text is visible without relying only on color.
- [ ] Operational log remains responsive and bounded during a sustained stream.
- [ ] UI queue/drop and recorder-drop counters remain visible.
- [ ] Closing during Ready, Streaming, and Recording performs orderly bounded shutdown.
- [ ] Keyboard focus and accessible names are manually inspected.

## Protocol cross-end validation

- [ ] Record system protocol authority commit `e4aa40b4d5dfc3e7f878f82f5a89115de9fe3679` and confirm both PC and MCU consume protocol v0.1.0 / wire version 0x01.
- [ ] MCU implementation passes the shared frame vectors.
- [ ] PING, GET_DEVICE_INFO, SET_STREAM_CONFIG, START_STREAM, and STOP_STREAM sequences, payloads, allowed states, ACK/NACK Node state, and success transitions match.
- [ ] CRC coverage, little-endian fields, payload limits, and strict lengths match.
- [ ] Unknown IDs, malformed command payloads, invalid state, unsupported version, duplicate response, unmatched sequence, and 250 ms partial-frame behavior match the Project Protocol.

## Sustained physical transport

- [ ] Record COM port, selected baud, board identity, firmware commit/version, and test duration.
- [ ] Confirm the USB-UART/VCP adapter, Windows driver, and MCU UART clock configuration support the selected baud before sustained testing.
- [ ] Stream 200 Hz telemetry for the approved duration.
- [ ] Record frame/sample counts, CRC errors, format errors, unknown IDs, lost samples, UI drops, and recorder drops.
- [ ] Verify CSV monotonic sample counter/device tick and record any gaps.
- [ ] Exercise USB disconnect while idle, streaming, and recording.
- [ ] Reconnect with a new session generation and confirm no stale response is accepted.
- [ ] Repeat start/stop cycles and application close cycles.

## Evidence classification

- [ ] Label Fake Device results as simulator evidence.
- [ ] Label physical NUCLEO/VCP results as target integration evidence.
- [ ] Do not claim safety, product, clinical, or regulatory validation from this PoC checklist.
