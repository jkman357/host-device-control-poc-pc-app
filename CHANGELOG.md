# Changelog

## 0.1.2 - 2026-07-25

- fix the WPF startup exception caused by a default TwoWay binding to read-only ViewModel status properties;
- explicitly mark all display-only status, counter, waveform, and log bindings as `Mode=OneWay`;
- document the binding rule in the README.

## 0.1.1 - 2026-07-25

- make `HostDeviceControl.App` the first project in the solution so a fresh Visual Studio workspace defaults to the executable project;
- add a shared `.slnLaunch` profile for `HostDeviceControl.App`;
- document how to correct the startup project when Visual Studio selects a class library.

## 0.1.0 - 2026-07-25

Initial PoC baseline:

- WPF desktop application;
- fake-device and serial transports;
- binary framing and CRC implementation;
- device-session state machine;
- handshake, stream configuration, start, and stop commands;
- 200 Hz sine-wave telemetry;
- live waveform and CSV recording;
- protocol test vectors and executable self-tests;
- Windows CI workflow.
