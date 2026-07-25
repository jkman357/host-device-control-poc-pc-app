using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using HostDeviceControl.Core.Abstractions;

namespace HostDeviceControl.Transport.Serial;

public sealed class SerialDeviceTransport : IDeviceTransport
{
    private readonly SerialTransportOptions _options;
    private SerialPort? _serialPort;
    private bool _disposed;

    public SerialDeviceTransport(SerialTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsConnected => _serialPort?.IsOpen == true;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (IsConnected)
        {
            throw new InvalidOperationException("Serial transport is already connected.");
        }

        var serialPort = new SerialPort(
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
            WriteTimeout = 1000,
            ReadBufferSize = 8192,
            WriteBufferSize = 2048
        };

        try
        {
            serialPort.Open();
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();
            _serialPort = serialPort;
        }
        catch
        {
            serialPort.Dispose();
            throw;
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SerialPort? serialPort = _serialPort;
        _serialPort = null;

        if (serialPort is null)
        {
            return Task.CompletedTask;
        }

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

        return Task.CompletedTask;
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

    private SerialPort GetOpenPort()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_serialPort is not { IsOpen: true } serialPort)
        {
            throw new InvalidOperationException("Serial transport is not connected.");
        }

        return serialPort;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
    }
}
