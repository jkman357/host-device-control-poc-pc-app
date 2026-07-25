// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Represents one validated host-device protocol frame.
/// </summary>
public sealed class ProtocolFrame
{
    private readonly byte[] _payload;

    /// <summary>
    /// Initializes an immutable frame snapshot. The payload is copied so later
    /// caller mutation cannot change the frame after validation or correlation.
    /// </summary>
    public ProtocolFrame(
        byte version,
        MessageType messageType,
        ushort sequence,
        byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!MessageTypeValidator.IsDefined((byte)messageType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(messageType),
                messageType,
                "Message type is not defined by the Project Protocol.");
        }

        if (payload.Length > ProtocolConstants.MaximumPayloadSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                payload.Length,
                "Payload exceeds the Project Protocol limit.");
        }

        Version = version;
        MessageType = messageType;
        Sequence = sequence;
        _payload = payload.AsSpan().ToArray();
    }

    /// <summary>
    /// Gets the protocol wire version.
    /// </summary>
    public byte Version { get; }

    /// <summary>
    /// Gets the validated message type.
    /// </summary>
    public MessageType MessageType { get; }

    /// <summary>
    /// Gets the request, response, or event sequence identifier.
    /// </summary>
    public ushort Sequence { get; }

    /// <summary>
    /// Gets an immutable view of the payload bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Payload => _payload;
}
