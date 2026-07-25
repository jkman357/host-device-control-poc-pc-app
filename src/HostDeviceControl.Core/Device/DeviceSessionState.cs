// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

namespace HostDeviceControl.Core.Device;

/// <summary>
/// Explicit lifecycle states for one Coordinator-to-Node session.
/// </summary>
public enum DeviceSessionState
{
    Disconnected = 0,
    Connecting,
    Handshaking,
    Ready,
    StartingStream,
    Streaming,
    StoppingStream,
    Disconnecting,
    Faulted
}
