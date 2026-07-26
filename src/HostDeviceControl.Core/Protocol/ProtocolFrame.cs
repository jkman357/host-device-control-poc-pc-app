// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;

namespace HostDeviceControl.Core.Protocol;

/// <summary>
/// Represents one immutable host-device protocol frame.
/// </summary>
public sealed class ProtocolFrame
{
    private readonly byte[] _payload;

    /// <summary>
    /// Initializes an immutable frame snapshot for a message type defined by
    /// the Project Protocol. The payload is copied so later caller mutation
    /// cannot change the frame after validation or correlation.
    /// </summary>
    public ProtocolFrame(
        byte version,
        MessageType messageType,
        ushort sequence,
        byte[] payload)
        : this(
            version,
            messageType,
            sequence,
            payload,
            allowUnknownMessageType: false)
    {
    }

    private ProtocolFrame(
        byte version,
        MessageType messageType,
        ushort sequence,
        byte[] payload,
        bool allowUnknownMessageType)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!allowUnknownMessageType &&
            !MessageTypeValidator.IsDefined((byte)messageType))
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
    /// Creates a frame decoded from a validated wire envelope. A permissive
    /// fake-Node decoder may preserve an unknown raw request identifier so the
    /// simulator can return the protocol-defined INVALID_COMMAND NACK.
    /// </summary>
    internal static ProtocolFrame CreateDecoded(
        byte version,
        byte rawMessageType,
        ushort sequence,
        byte[] payload,
        bool allowUnknownMessageType)
    {
        return new ProtocolFrame(
            version,
            (MessageType)rawMessageType,
            sequence,
            payload,
            allowUnknownMessageType);
    }

    /// <summary>
    /// Gets the protocol wire version.
    /// </summary>
    public byte Version { get; }

    /// <summary>
    /// Gets the raw message identifier represented by the protocol enum.
    /// Normal application decoders expose only defined values; a permissive
    /// fake-Node decoder may expose an undefined request identifier.
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
