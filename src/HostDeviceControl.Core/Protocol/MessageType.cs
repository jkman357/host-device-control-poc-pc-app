namespace HostDeviceControl.Core.Protocol;

public enum MessageType : byte
{
    Ping = 0x01,
    GetDeviceInfo = 0x02,
    SetStreamConfig = 0x03,
    StartStream = 0x04,
    StopStream = 0x05,

    Ack = 0x80,
    Nack = 0x81,
    DeviceInfo = 0x82,
    DeviceStatus = 0x83,

    TelemetrySample = 0x90,
    ErrorReport = 0x91
}
