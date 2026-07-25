using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HostDeviceControl.Core.Abstractions;
using HostDeviceControl.Core.Models;
using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Transport.Fake;

public sealed class FakeDeviceTransport : IDeviceTransport
{
    private const ushort DeviceType = 0x4460;
    private const ushort MaximumStreamRateHz = 1000;
    private const double SineFrequencyHz = 1.0;

    private readonly Channel<byte> _receiveBytes = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(65536)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly FrameDecoder _hostFrameDecoder = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private CancellationTokenSource? _streamCancellation;
    private Task? _streamTask;
    private ushort _streamIntervalUs = ProtocolConstants.DefaultStreamIntervalUs;
    private ushort _deviceFrameSequence;
    private uint _sampleCounter;
    private uint _deviceTickUs;
    private bool _isConnected;
    private bool _disposed;

    public bool IsConnected => _isConnected;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_isConnected)
        {
            throw new InvalidOperationException("Fake transport is already connected.");
        }

        _isConnected = true;
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (!_isConnected)
        {
            return;
        }

        await StopStreamingAsync().ConfigureAwait(false);
        _isConnected = false;
        _receiveBytes.Writer.TryComplete();
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        EnsureConnected();

        if (buffer.IsEmpty)
        {
            return 0;
        }

        byte firstByte = await _receiveBytes.Reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        SetBufferByte(buffer, 0, firstByte);
        int count = 1;

        while ((count < buffer.Length) &&
               _receiveBytes.Reader.TryRead(out byte value))
        {
            SetBufferByte(buffer, count, value);
            count++;
        }

        return count;
    }

    public async ValueTask WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        byte[] dataCopy = data.ToArray();
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _hostFrameDecoder.Append(dataCopy);

            while (_hostFrameDecoder.TryRead(out ProtocolFrame? frame))
            {
                if (frame is not null)
                {
                    await HandleHostFrameAsync(frame, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task HandleHostFrameAsync(
        ProtocolFrame frame,
        CancellationToken cancellationToken)
    {
        if (frame.Version != ProtocolConstants.WireVersion)
        {
            await SendNackAsync(
                frame,
                ResultCode.UnsupportedVersion,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (frame.MessageType)
        {
            case MessageType.Ping:
                await SendAckAsync(frame, cancellationToken).ConfigureAwait(false);
                break;

            case MessageType.GetDeviceInfo:
                await SendDeviceInfoAsync(frame.Sequence, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case MessageType.SetStreamConfig:
                await HandleSetStreamConfigAsync(frame, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case MessageType.StartStream:
                if (_streamTask is not null)
                {
                    await SendNackAsync(
                        frame,
                        ResultCode.InvalidState,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await SendAckAsync(frame, cancellationToken).ConfigureAwait(false);
                    StartStreaming();
                }

                break;

            case MessageType.StopStream:
                if (_streamTask is null)
                {
                    await SendNackAsync(
                        frame,
                        ResultCode.InvalidState,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await StopStreamingAsync().ConfigureAwait(false);
                    await SendAckAsync(frame, cancellationToken).ConfigureAwait(false);
                }

                break;

            default:
                await SendNackAsync(
                    frame,
                    ResultCode.InvalidCommand,
                    cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleSetStreamConfigAsync(
        ProtocolFrame frame,
        CancellationToken cancellationToken)
    {
        ushort intervalUs;

        try
        {
            intervalUs = PayloadCodec.DecodeSetStreamConfig(frame.Payload);
        }
        catch (ProtocolException)
        {
            await SendNackAsync(
                frame,
                ResultCode.InvalidLength,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if ((intervalUs < ProtocolConstants.MinimumStreamIntervalUs) ||
            (intervalUs > ProtocolConstants.MaximumStreamIntervalUs))
        {
            await SendNackAsync(
                frame,
                ResultCode.InvalidValue,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_streamTask is not null)
        {
            await SendNackAsync(
                frame,
                ResultCode.InvalidState,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        _streamIntervalUs = intervalUs;
        await SendAckAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendDeviceInfoAsync(
        ushort sequence,
        CancellationToken cancellationToken)
    {
        var deviceInfo = new DeviceInfo(
            DeviceType,
            0,
            1,
            0,
            MaximumStreamRateHz,
            "NUCLEO-F446RE-FAKE");
        var response = new ProtocolFrame(
            ProtocolConstants.WireVersion,
            MessageType.DeviceInfo,
            sequence,
            PayloadCodec.EncodeDeviceInfo(deviceInfo));

        await QueueFrameAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private Task SendAckAsync(
        ProtocolFrame request,
        CancellationToken cancellationToken)
    {
        var response = new ProtocolFrame(
            ProtocolConstants.WireVersion,
            MessageType.Ack,
            request.Sequence,
            PayloadCodec.EncodeAck(request.MessageType, ResultCode.Ok));
        return QueueFrameAsync(response, cancellationToken);
    }

    private Task SendNackAsync(
        ProtocolFrame request,
        ResultCode resultCode,
        CancellationToken cancellationToken)
    {
        var response = new ProtocolFrame(
            ProtocolConstants.WireVersion,
            MessageType.Nack,
            request.Sequence,
            PayloadCodec.EncodeAck(request.MessageType, resultCode));
        return QueueFrameAsync(response, cancellationToken);
    }

    private void StartStreaming()
    {
        _streamCancellation = new CancellationTokenSource();
        CancellationToken streamToken = _streamCancellation.Token;
        _streamTask = Task.Run(
            () => StreamLoopAsync(streamToken),
            CancellationToken.None);
    }

    private async Task StopStreamingAsync()
    {
        CancellationTokenSource? cancellation = _streamCancellation;
        Task? streamTask = _streamTask;

        _streamCancellation = null;
        _streamTask = null;

        if (cancellation is null || streamTask is null)
        {
            return;
        }

        cancellation.Cancel();

        try
        {
            await streamTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task StreamLoopAsync(CancellationToken cancellationToken)
    {
        TimeSpan interval = TimeSpan.FromMilliseconds(_streamIntervalUs / 1000.0);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            _sampleCounter = unchecked(_sampleCounter + 1U);
            _deviceTickUs = unchecked(_deviceTickUs + _streamIntervalUs);

            double timeSeconds = _deviceTickUs / 1_000_000.0;
            float value = (float)Math.Sin(
                2.0 * Math.PI * SineFrequencyHz * timeSeconds);

            var sample = new TelemetrySample(
                _sampleCounter,
                _deviceTickUs,
                value,
                0,
                DateTimeOffset.UtcNow);
            var frame = new ProtocolFrame(
                ProtocolConstants.WireVersion,
                MessageType.TelemetrySample,
                NextDeviceFrameSequence(),
                PayloadCodec.EncodeTelemetry(sample));

            await QueueFrameAsync(frame, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task QueueFrameAsync(
        ProtocolFrame frame,
        CancellationToken cancellationToken)
    {
        byte[] encoded = FrameEncoder.Encode(frame);

        foreach (byte value in encoded)
        {
            await _receiveBytes.Writer
                .WriteAsync(value, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private ushort NextDeviceFrameSequence()
    {
        _deviceFrameSequence = unchecked((ushort)(_deviceFrameSequence + 1));
        return _deviceFrameSequence;
    }


    private static void SetBufferByte(Memory<byte> buffer, int index, byte value)
    {
        buffer.Span[index] = value;
    }

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isConnected)
        {
            throw new InvalidOperationException("Fake transport is not connected.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_isConnected)
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _writeGate.Dispose();
        _disposed = true;
    }
}
