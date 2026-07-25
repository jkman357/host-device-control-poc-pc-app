# Host-Device Control PoC PC App

Windows WPF host application for the `NUCLEO-F446RE` host-device-control proof of concept.

The application demonstrates a complete vertical slice:

- transport-independent device session;
- framed binary protocol with CRC-16/CCITT-FALSE;
- command/response correlation and timeout handling;
- 200 Hz telemetry acquisition;
- decoupled 20 Hz UI refresh;
- live waveform display;
- CSV recording;
- fake-device mode for PC-side development before MCU firmware is ready;
- serial mode for the ST-LINK Virtual COM Port at 115200 bps.

## Status

`v0.1.2 PoC baseline`

The repository is intentionally usable before the MCU implementation is complete. The authoritative communication contract is:

```text
protocol/protocol.yaml
```

PC and MCU implementations shall conform to the same protocol version and test vectors.

## Prerequisites

- Windows 10 or Windows 11
- Visual Studio 2022 17.11 or later (including newer Visual Studio releases), or .NET 8 SDK
- `.NET desktop development` workload when using Visual Studio

## Build

```powershell
dotnet restore HostDeviceControl.Poc.sln
dotnet build HostDeviceControl.Poc.sln -c Release
```


## Run from Visual Studio

The executable project is:

```text
HostDeviceControl.App
```

After opening `HostDeviceControl.Poc.sln`, confirm that `HostDeviceControl.App` is selected as the startup project. If Visual Studio selects a class-library project instead:

1. In Solution Explorer, right-click `HostDeviceControl.App`.
2. Select `Set as Startup Project`.
3. Press `F5` or `Ctrl+F5`.

The repository also includes `HostDeviceControl.Poc.slnLaunch` for Visual Studio versions that support shared solution launch profiles.

### WPF binding startup error

Display-only ViewModel properties use explicit `Mode=OneWay` bindings. This avoids WPF attempting to write back to properties whose setters are intentionally private, especially when values are displayed through `Run.Text`.

## Run without hardware

```powershell
./scripts/run-fake.ps1
```

Equivalent direct command:

```powershell
dotnet run --project src/HostDeviceControl.App/HostDeviceControl.App.csproj
```

Then:

1. Select `Fake Device`.
2. Select `Connect`.
3. Select `Start Stream`.
4. Confirm that the waveform, sample count, and device tick update.

## Run with NUCLEO-F446RE

1. Connect the NUCLEO board through the ST-LINK USB connector.
2. Confirm the ST-LINK Virtual COM Port in Windows Device Manager.
3. Select `Serial Port` in the application.
4. Select the COM port and keep the baud rate at `115200`.
5. Connect and start streaming.

The MCU firmware must implement protocol version `0x01` from `protocol/protocol.yaml`.

## Protocol self-test

The test project requires no third-party test framework:

```powershell
./scripts/test.ps1
```

Equivalent direct command:

```powershell
dotnet run --project tests/HostDeviceControl.Protocol.Tests/HostDeviceControl.Protocol.Tests.csproj -c Release
```

It checks:

- the standard CRC test vector;
- frame encoding and decoding;
- fragmented input;
- garbage-byte resynchronization;
- CRC rejection;
- command/response and fake-device telemetry flow.

## Repository structure

```text
src/
  HostDeviceControl.App/              WPF UI and application composition
  HostDeviceControl.Core/             protocol, session, models, abstractions
  HostDeviceControl.Transport.Fake/   deterministic MCU simulator
  HostDeviceControl.Transport.Serial/ serial-port transport
tests/
  HostDeviceControl.Protocol.Tests/   dependency-free executable tests
protocol/
  protocol.yaml                       authoritative PC/MCU contract
  test-vectors/                       cross-language frame examples
docs/
  Architecture.md
  Protocol_Decisions.md
  Bringup_Checklist.md
```

## Responsibility boundaries

- `App` does not parse bytes or calculate CRC.
- `Core` does not depend on WPF or `SerialPort`.
- `Transport.Serial` moves bytes only and does not interpret messages.
- `Transport.Fake` behaves like a device and is replaceable by the real serial transport.
- `protocol/protocol.yaml` owns message IDs, payload layouts, framing, byte order, and CRC parameters.

## Known PoC limitations

- one device session at a time;
- no automatic reconnect;
- serial discovery uses COM-port names only;
- protocol negotiation is limited to exact version `0x01`;
- CSV recording is intended for engineering evaluation, not regulated production data retention;
- no installer or code signing is included.

## License

No open-source license is granted. See `LICENSE` and `NOTICE.md`.
