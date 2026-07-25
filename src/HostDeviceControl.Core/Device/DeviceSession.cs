using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HostDeviceControl.Core.Abstractions;
using HostDeviceControl.Core.Models;
using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Core.Device;

public sealed class DeviceSession : IAsyncDisposable
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopCommandTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly IDeviceTransport _transport;
    private readonly FrameDecoder _decoder = new();
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<ProtocolFrame>>
        _pendingResponses = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveTask;
    private int _stateValue = (int)DeviceSessionState.Disconnected;
    private int _sequenceValue;
    private long _receivedFrameCount;
    private long _lostSampleCount;
    private uint? _lastSampleCounter;
    private bool _disposed;

    public DeviceSession(IDeviceTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public event Action<DeviceSessionState>? StateChanged;

    public event Action<TelemetrySample>? TelemetryReceived;

    public event Action<string>? DiagnosticMessage;

    public DeviceSessionState State =>
        (DeviceSessionState)Volatile.Read(ref _stateValue);

    public DeviceInfo? DeviceInfo { get; private set; }

    public long ReceivedFrameCount => Interlocked.Read(ref _receivedFrameCount);

    public long LostSampleCount => Interlocked.Read(ref _lostSampleCount);

    public long CrcErrorCount => _decoder.CrcErrorCount;

    public long FormatErrorCount => _decoder.FormatErrorCount;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureState(DeviceSessionState.Disconnected);
            SetState(DeviceSessionState.Connecting);

            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

            _receiveCancellation = new CancellationTokenSource();
            CancellationToken receiveToken = _receiveCancellation.Token;
            _receiveTask = Task.Run(
                () => ReceiveLoopAsync(receiveToken),
                CancellationToken.None);

            SetState(DeviceSessionState.Handshaking);
            ProtocolFrame response = await SendRequestAsync(
                MessageType.GetDeviceInfo,
                [],
                DefaultCommandTimeout,
                cancellationToken,
                MessageType.DeviceInfo).ConfigureAwait(false);

            DeviceInfo = PayloadCodec.DecodeDeviceInfo(response.Payload);
            DiagnosticMessage?.Invoke(
                $"Handshake completed: {DeviceInfo.DeviceName} FW {DeviceInfo.FirmwareVersion}.");
            SetState(DeviceSessionState.Ready);
        }
        catch
        {
            SetState(DeviceSessionState.Faulted);
            await DisconnectCoreAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StartStreamingAsync(
        ushort intervalUs,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureState(DeviceSessionState.Ready);

            if ((intervalUs < ProtocolConstants.MinimumStreamIntervalUs) ||
                (intervalUs > ProtocolConstants.MaximumStreamIntervalUs))
            {
                throw new ArgumentOutOfRangeException(nameof(intervalUs));
            }

            SetState(DeviceSessionState.StartingStream);

            await SendRequestAsync(
                MessageType.SetStreamConfig,
                PayloadCodec.EncodeSetStreamConfig(intervalUs),
                DefaultCommandTimeout,
                cancellationToken,
                MessageType.Ack).ConfigureAwait(false);

            await SendRequestAsync(
                MessageType.StartStream,
                [],
                DefaultCommandTimeout,
                cancellationToken,
                MessageType.Ack).ConfigureAwait(false);

            _lastSampleCounter = null;
            SetState(DeviceSessionState.Streaming);
            DiagnosticMessage?.Invoke($"Streaming started at {intervalUs} us interval.");
        }
        catch
        {
            if (State != DeviceSessionState.Faulted)
            {
                SetState(DeviceSessionState.Ready);
            }

            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopStreamingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureState(DeviceSessionState.Streaming);
            SetState(DeviceSessionState.StoppingStream);

            await SendRequestAsync(
                MessageType.StopStream,
                [],
                StopCommandTimeout,
                cancellationToken,
                MessageType.Ack).ConfigureAwait(false);

            SetState(DeviceSessionState.Ready);
            DiagnosticMessage?.Invoke("Streaming stopped.");
        }
        catch
        {
            SetState(DeviceSessionState.Faulted);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<ProtocolFrame> SendRequestAsync(
        MessageType requestType,
        byte[] payload,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params MessageType[] validResponseTypes)
    {
        if (!_transport.IsConnected)
        {
            throw new InvalidOperationException("Transport is not connected.");
        }

        ushort sequence = NextSequence();
        var completion = new TaskCompletionSource<ProtocolFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingResponses.TryAdd(sequence, completion))
        {
            throw new InvalidOperationException("Unable to allocate command sequence.");
        }

        try
        {
            var request = new ProtocolFrame(
                ProtocolConstants.WireVersion,
                requestType,
                sequence,
                payload);
            byte[] encoded = FrameEncoder.Encode(request);

            await _transport.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
            DiagnosticMessage?.Invoke($"TX {requestType} seq={sequence}.");

            ProtocolFrame response = await completion.Task
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);

            if (response.MessageType == MessageType.Nack)
            {
                (MessageType rejectedRequest, ResultCode resultCode) =
                    PayloadCodec.DecodeAck(response.Payload);
                throw new DeviceCommandException(
                    rejectedRequest,
                    resultCode,
                    $"Device rejected {rejectedRequest}: {resultCode}.");
            }

            if (!validResponseTypes.Contains(response.MessageType))
            {
                throw new ProtocolException(
                    $"Unexpected response {response.MessageType} for {requestType}.");
            }

            if (response.MessageType == MessageType.Ack)
            {
                (MessageType acknowledgedRequest, ResultCode resultCode) =
                    PayloadCodec.DecodeAck(response.Payload);

                if ((acknowledgedRequest != requestType) ||
                    (resultCode != ResultCode.Ok))
                {
                    throw new ProtocolException(
                        $"ACK does not match request {requestType}.");
                }
            }

            return response;
        }
        finally
        {
            _pendingResponses.TryRemove(sequence, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] receiveBuffer = new byte[1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int receivedLength = await _transport.ReadAsync(
                    receiveBuffer,
                    cancellationToken).ConfigureAwait(false);

                if (receivedLength <= 0)
                {
                    continue;
                }

                AppendReceivedBytes(_decoder, receiveBuffer, receivedLength);

                while (_decoder.TryRead(out ProtocolFrame? frame))
                {
                    if (frame is not null)
                    {
                        HandleFrame(frame);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception) when (
            cancellationToken.IsCancellationRequested ||
            State == DeviceSessionState.Disconnecting)
        {
        }
        catch (Exception exception)
        {
            DiagnosticMessage?.Invoke($"Receive loop failed: {exception.Message}");

            foreach (TaskCompletionSource<ProtocolFrame> completion in
                     _pendingResponses.Values)
            {
                completion.TrySetException(exception);
            }

            SetState(DeviceSessionState.Faulted);
        }
    }


    private static void AppendReceivedBytes(
        FrameDecoder decoder,
        byte[] receiveBuffer,
        int receivedLength)
    {
        decoder.Append(receiveBuffer.AsSpan(0, receivedLength));
    }

    private void HandleFrame(ProtocolFrame frame)
    {
        Interlocked.Increment(ref _receivedFrameCount);

        if (frame.Version != ProtocolConstants.WireVersion)
        {
            DiagnosticMessage?.Invoke(
                $"Ignored unsupported protocol version 0x{frame.Version:X2}.");
            return;
        }

        if (frame.MessageType == MessageType.TelemetrySample)
        {
            try
            {
                TelemetrySample sample = PayloadCodec.DecodeTelemetry(
                    frame.Payload,
                    DateTimeOffset.UtcNow);
                UpdateLossCount(sample.SampleCounter);
                TelemetryReceived?.Invoke(sample);
            }
            catch (ProtocolException exception)
            {
                DiagnosticMessage?.Invoke(exception.Message);
            }

            return;
        }

        if (_pendingResponses.TryGetValue(frame.Sequence, out var completion))
        {
            completion.TrySetResult(frame);
            return;
        }

        DiagnosticMessage?.Invoke(
            $"Unmatched RX {frame.MessageType} seq={frame.Sequence}.");
    }

    private void UpdateLossCount(uint sampleCounter)
    {
        if (_lastSampleCounter.HasValue)
        {
            uint expected = unchecked(_lastSampleCounter.Value + 1U);
            uint difference = unchecked(sampleCounter - expected);

            if ((difference > 0U) && (difference < 0x80000000U))
            {
                Interlocked.Add(ref _lostSampleCount, difference);
            }
        }

        _lastSampleCounter = sampleCounter;
    }

    private async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        DeviceSessionState currentState = State;
        if (currentState == DeviceSessionState.Disconnected)
        {
            return;
        }

        SetState(DeviceSessionState.Disconnecting);

        _receiveCancellation?.Cancel();

        foreach (TaskCompletionSource<ProtocolFrame> completion in
                 _pendingResponses.Values)
        {
            completion.TrySetCanceled();
        }

        _pendingResponses.Clear();

        if (_transport.IsConnected)
        {
            await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _receiveCancellation?.Dispose();
        _receiveCancellation = null;
        _receiveTask = null;
        DeviceInfo = null;
        _lastSampleCounter = null;
        SetState(DeviceSessionState.Disconnected);
        DiagnosticMessage?.Invoke("Disconnected.");
    }

    private ushort NextSequence()
    {
        while (true)
        {
            int value = Interlocked.Increment(ref _sequenceValue);
            ushort sequence = unchecked((ushort)value);

            if (sequence != 0)
            {
                return sequence;
            }
        }
    }

    private void EnsureState(DeviceSessionState requiredState)
    {
        if (State != requiredState)
        {
            throw new InvalidOperationException(
                $"Operation requires {requiredState}; current state is {State}.");
        }
    }

    private void SetState(DeviceSessionState state)
    {
        DeviceSessionState previous =
            (DeviceSessionState)Interlocked.Exchange(ref _stateValue, (int)state);

        if (previous != state)
        {
            StateChanged?.Invoke(state);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
        _disposed = true;
    }
}
