// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace HostDeviceControl.Core.Abstractions;

/// <summary>
/// Moves an ordered byte stream without interpreting protocol message meaning.
/// One session owns one transport instance and its asynchronous lifetime.
/// </summary>
public interface IDeviceTransport : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the underlying byte channel is currently open.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// Opens the byte channel. Cancellation semantics are adapter-specific and
    /// shall be documented by the concrete transport.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes the byte channel after the owning session has cancelled receive
    /// work.
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads zero or more ordered bytes. A return value of zero does not imply a
    /// complete protocol frame.
    /// </summary>
    public ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes all supplied bytes to the transport. Completion is not a protocol
    /// acknowledgement.
    /// </summary>
    public ValueTask WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken);
}
