using System;

namespace HostDeviceControl.Core.Protocol;

public sealed record ProtocolFrame(
    byte Version,
    MessageType MessageType,
    ushort Sequence,
    byte[] Payload)
{
    public ReadOnlyMemory<byte> PayloadMemory => Payload;
}
