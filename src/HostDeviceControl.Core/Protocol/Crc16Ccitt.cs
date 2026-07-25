// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Implements CRC-16/CCITT-FALSE using the parameters defined by the Project
/// Protocol.
/// </summary>
public static class Crc16Ccitt
{
    private const ushort Polynomial = 0x1021;
    private const ushort InitialValue = 0xFFFF;
    private const ushort HighBitMask = 0x8000;
    private const int BitsPerByte = 8;

    /// <summary>
    /// Computes CRC-16/CCITT-FALSE over the supplied bytes.
    /// </summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = InitialValue;

        foreach (byte value in data)
        {
            crc ^= checked((ushort)(value << BitsPerByte));

            for (int bitIndex = 0; bitIndex < BitsPerByte; bitIndex++)
            {
                bool isHighBitSet = (crc & HighBitMask) != 0;
                crc <<= 1;

                if (isHighBitSet)
                {
                    crc ^= Polynomial;
                }
            }
        }

        return crc;
    }
}
