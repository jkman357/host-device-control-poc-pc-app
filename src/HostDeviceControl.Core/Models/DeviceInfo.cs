namespace HostDeviceControl.Core.Models;

public sealed record DeviceInfo(
    ushort DeviceType,
    byte FirmwareMajor,
    byte FirmwareMinor,
    byte FirmwarePatch,
    ushort MaximumStreamRateHz,
    string DeviceName)
{
    public string FirmwareVersion =>
        $"{FirmwareMajor}.{FirmwareMinor}.{FirmwarePatch}";
}
