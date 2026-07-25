using System.IO.Ports;

namespace HostDeviceControl.Transport.Serial;

public sealed record SerialTransportOptions(
    string PortName,
    int BaudRate = 115200,
    Parity Parity = Parity.None,
    int DataBits = 8,
    StopBits StopBits = StopBits.One,
    Handshake Handshake = Handshake.None);
