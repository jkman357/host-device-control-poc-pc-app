namespace HostDeviceControl.Core.Device;

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
