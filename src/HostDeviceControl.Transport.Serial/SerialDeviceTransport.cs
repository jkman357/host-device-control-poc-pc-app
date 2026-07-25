// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using HostDeviceControl.Core.Abstractions;

namespace HostDeviceControl.Transport.Serial;

/// <summary>
/// Owns one Windows <see cref="SerialPort"/> and exposes only ordered byte I/O.
/// Protocol interpretation remains in the Core session and codec layers.
/// </summary>
public sealed class SerialDeviceTransport : IDeviceTransport
{
    private const int WriteTimeoutMilliseconds = 1000;
    private const int ReadBufferSizeBytes = 8192;
    private const int WriteBufferSizeBytes = 2048;

    private readonly SerialTransportOptions _options;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private SerialPort? _serialPort;
    private bool _disposed;

    public SerialDeviceTransport(SerialTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsConnected => _serialPort?.IsOpen == true;

    /// <summary>
    /// Opens the port on a worker thread because <see cref="SerialPort.Open"/>
    /// is a blocking platform API. Cancellation is checked before and after the
    /// OS call; the underlying API does not provide an in-call cancellation
    /// mechanism.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsConnected)
            {
                throw new InvalidOperationException(
                    "Serial transport is already connected.");
            }

            SerialPort serialPort = CreateSerialPort();

            try
            {
                await Task.Run(serialPort.Open, CancellationToken.None)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();
                _serialPort = serialPort;
            }
            catch
            {
                serialPort.Dispose();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Closes and disposes the port on a worker thread after the owning session
    /// has cancelled its receive operation.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SerialPort? serialPort = _serialPort;
            _serialPort = null;

            if (serialPort is null)
            {
                return;
            }

            await Task.Run(
                () => CloseAndDispose(serialPort),
                CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        SerialPort serialPort = GetOpenPort();
        Stream stream = serialPort.BaseStream;
        return stream.ReadAsync(buffer, cancellationToken);
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        SerialPort serialPort = GetOpenPort();
        Stream stream = serialPort.BaseStream;
        return stream.WriteAsync(data, cancellationToken);
    }

    private SerialPort CreateSerialPort()
    {
        return new SerialPort(
            _options.PortName,
            _options.BaudRate,
            _options.Parity,
            _options.DataBits,
            _options.StopBits)
        {
            Handshake = _options.Handshake,
            DtrEnable = false,
            RtsEnable = false,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = WriteTimeoutMilliseconds,
            ReadBufferSize = ReadBufferSizeBytes,
            WriteBufferSize = WriteBufferSizeBytes
        };
    }

    private SerialPort GetOpenPort()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_serialPort is not { IsOpen: true } serialPort)
        {
            throw new InvalidOperationException(
                "Serial transport is not connected.");
        }

        return serialPort;
    }

    private static void CloseAndDispose(SerialPort serialPort)
    {
        try
        {
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
        finally
        {
            serialPort.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _lifecycleGate.Dispose();
        _disposed = true;
    }
}
