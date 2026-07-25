using System;

namespace HostDeviceControl.Core.Protocol;

public static class Crc16Ccitt
{
    private const ushort Polynomial = 0x1021;
    private const ushort InitialValue = 0xFFFF;

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = InitialValue;

        foreach (byte value in data)
        {
            crc ^= (ushort)(value << 8);

            for (int bitIndex = 0; bitIndex < 8; bitIndex++)
            {
                bool highBitSet = (crc & 0x8000) != 0;
                crc <<= 1;

                if (highBitSet)
                {
                    crc ^= Polynomial;
                }
            }
        }

        return crc;
    }
}
