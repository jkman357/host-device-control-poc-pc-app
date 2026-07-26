# Concurrency Model

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

| Work item | Owner | Bound | Overflow / timeout policy | Shutdown |
|---|---|---:|---|---|
| Transport receive loop | `DeviceSession` connection generation | one task | read errors fault the generation | cancelled, transport closed, awaited with timeout |
| Pending commands | `DeviceSession` | 8 | reject new request; each request has timeout/cancellation; ambiguous state-command outcomes trigger a fresh bounded PING | all retired on disconnect/generation change |
| Decoder bytes | `FrameDecoder` | 65,536 bytes | discard/resynchronize and count format loss | generation-owned object retained without external work |
| Fake receive bytes | `FakeDeviceTransport` | 65,536 bytes | producer waits; no silent byte loss | writer completed and loop awaited |
| UI telemetry | `MainViewModel` | 2,048 samples | drop oldest; expose drop count | cleared after timer stops |
| Operational diagnostics | `MainViewModel` | 256 messages | drop oldest; expose log-drop count | drained on UI timer |
| UI drain | WPF dispatcher | 512 samples per 50 ms | remaining samples stay bounded | timer stopped first |
| CSV telemetry | `CsvTelemetryRecorder` | 4,096 samples | reject write, count drop, surface incomplete recording | channel completed and writer awaited |

Connection generations prevent responses from a retired session from satisfying requests in a later session. State-changing command recovery uses a fresh cancellation source rather than the already-cancelled caller token; if PING recovery also fails, the cached Node state is cleared and the session becomes Faulted. Cancellation distinguishes expected application shutdown from unexpected failure. UI work is marshalled explicitly through the WPF dispatcher; the acquisition path never performs chart rendering or file I/O.
