namespace HostDeviceControl.Core.Protocol;

public enum ResultCode : byte
{
    Ok = 0x00,
    InvalidCommand = 0x01,
    InvalidLength = 0x02,
    InvalidValue = 0x03,
    InvalidState = 0x04,
    UnsupportedVersion = 0x05,
    InternalError = 0x06
}
