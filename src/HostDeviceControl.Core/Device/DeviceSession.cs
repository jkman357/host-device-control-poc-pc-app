// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HostDeviceControl.Core.Abstractions;
using HostDeviceControl.Core.Diagnostics;
using HostDeviceControl.Core.Models;
using HostDeviceControl.Core.Protocol;

namespace HostDeviceControl.Core.Device;

/// <summary>
/// Owns one transport, one receive loop, request correlation, protocol state,
/// and deterministic shutdown for a single Coordinator-to-Node relationship.
/// </summary>
public sealed class DeviceSession : IAsyncDisposable
{
    private const int MaximumPendingRequestCount = 8;
    private const int RecentResponseCapacity = 64;

    private readonly IDeviceTransport _transport;
    private readonly DeviceSessionOptions _options;
    private readonly TimeProvider _timeProvider;
    private FrameDecoder _decoder = new();
    private readonly ConcurrentDictionary<ushort, PendingRequest> _pendingResponses = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _recentResponseGate = new();
    private readonly Queue<ushort> _recentResponseOrder = new();
    private readonly HashSet<ushort> _recentResponseSequences = [];

    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveTask;
    private int _stateValue = (int)DeviceSessionState.Disconnected;
    private int _deviceStateValue = -1;
    private int _sequenceValue;
    private long _connectionGeneration;
    private long _receivedFrameCount;
    private long _lostSampleCount;
    private uint? _lastSampleCounter;
    private bool _disposed;

    /// <summary>
    /// Initializes a session and takes ownership of the supplied transport.
    /// </summary>
    public DeviceSession(
        IDeviceTransport transport,
        DeviceSessionOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? DeviceSessionOptions.Default;
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Raised after the authoritative session state changes.
    /// Subscribers are isolated so a presentation failure cannot terminate the
    /// receive loop.
    /// </summary>
    public event Action<DeviceSessionState>? StateChanged;

    /// <summary>
    /// Raised for each validated telemetry sample.
    /// </summary>
    public event Action<TelemetrySample>? TelemetryReceived;

    public event Action<DeviceOperatingState>? DeviceOperatingStateChanged;

    public event Action<DeviceStatus>? DeviceStatusReceived;

    public event Action<DeviceErrorReport>? DeviceErrorReported;

    /// <summary>
    /// Raised for bounded, single-line engineering diagnostics.
    /// </summary>
    public event Action<string>? DiagnosticMessage;

    public DeviceSessionState State =>
        (DeviceSessionState)Volatile.Read(ref _stateValue);

    public DeviceInfo? DeviceInfo { get; private set; }

    public DeviceOperatingState? DeviceState
    {
        get
        {
            int value = Volatile.Read(ref _deviceStateValue);
            return value < 0 ? null : (DeviceOperatingState)value;
        }
    }

    public long ConnectionGeneration =>
        Interlocked.Read(ref _connectionGeneration);

    public long ReceivedFrameCount =>
        Interlocked.Read(ref _receivedFrameCount);

    public long LostSampleCount =>
        Interlocked.Read(ref _lostSampleCount);

    public long CrcErrorCount => _decoder.CrcErrorCount;

    public long FormatErrorCount => _decoder.FormatErrorCount;

    public long UnknownMessageTypeCount => _decoder.UnknownMessageTypeCount;

    public long PartialFrameTimeoutCount => _decoder.PartialFrameTimeoutCount;

    /// <summary>
    /// Opens the transport, starts the owned receive loop, and completes the
    /// device-information handshake.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureState(DeviceSessionState.Disconnected);
            _decoder = new FrameDecoder();
            Interlocked.Exchange(ref _receivedFrameCount, 0);
            Interlocked.Exchange(ref _lostSampleCount, 0);
            _lastSampleCounter = null;
            SetDeviceState(null);
            ClearRecentResponses();
            long generation = Interlocked.Increment(ref _connectionGeneration);
            SetState(DeviceSessionState.Connecting);

            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

            _receiveCancellation = new CancellationTokenSource();
            CancellationToken receiveToken = _receiveCancellation.Token;
            _receiveTask = ReceiveLoopAsync(generation, receiveToken);

            SetState(DeviceSessionState.Handshaking);
            ProtocolFrame response = await SendRequestAsync(
                generation,
                MessageType.GetDeviceInfo,
                [],
                _options.GetDeviceInfoTimeout,
                cancellationToken,
                MessageType.DeviceInfo).ConfigureAwait(false);

            DeviceInfo = PayloadCodec.DecodeDeviceInfo(response.Payload.Span);
            PublishDiagnostic(
                $"Handshake completed: {DeviceInfo.DeviceName} FW " +
                $"{DeviceInfo.FirmwareVersion}.");
            SetDeviceState(DeviceOperatingState.Idle);
            SetState(DeviceSessionState.Ready);
        }
        catch
        {
            SetState(DeviceSessionState.Faulted);

            try
            {
                await DisconnectCoreAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                PublishDiagnostic(
                    $"Connection cleanup failed: {cleanupException.Message}");
            }

            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Sends PING while the Node is idle or streaming and requires a matching
    /// ACK carrying the current device state.
    /// </summary>
    public async Task PingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureState(DeviceSessionState.Ready, DeviceSessionState.Streaming);
            await SendRequestAsync(
                ConnectionGeneration,
                MessageType.Ping,
                [],
                _options.CommandTimeout,
                cancellationToken,
                MessageType.Ack).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Configures and starts telemetry streaming at the requested interval in
    /// microseconds.
    /// </summary>
    public async Task StartStreamingAsync(
        ushort intervalUs,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureState(DeviceSessionState.Ready);
            ValidateStreamInterval(intervalUs);
            long generation = ConnectionGeneration;

            SetState(DeviceSessionState.StartingStream);

            await SendRequestAsync(
                generation,
                MessageType.SetStreamConfig,
                PayloadCodec.EncodeSetStreamConfig(intervalUs),
                _options.CommandTimeout,
                cancellationToken,
                MessageType.Ack).ConfigureAwait(false);

            await SendRequestAsync(
                generation,
                MessageType.StartStream,
                [],
                _options.CommandTimeout,
                cancellationToken,
                MessageType.Ack).ConfigureAwait(false);

            _lastSampleCounter = null;
            SetState(DeviceSessionState.Streaming);
            PublishDiagnostic($"Streaming started at {intervalUs} us interval.");
        }
        catch (DeviceCommandException)
        {
            SynchronizeSessionStateFromDevice();
            throw;
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

    /// <summary>
    /// Requests an orderly end to telemetry streaming.
    /// </summary>
    public async Task StopStreamingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureState(DeviceSessionState.Streaming);
            long generation = ConnectionGeneration;
            SetState(DeviceSessionState.StoppingStream);

            await SendRequestAsync(
                generation,
                MessageType.StopStream,
                [],
                _options.StopStreamTimeout,
                cancellationToken,
                MessageType.Ack).ConfigureAwait(false);

            SetState(DeviceSessionState.Ready);
            PublishDiagnostic("Streaming stopped.");
        }
        catch (DeviceCommandException)
        {
            SynchronizeSessionStateFromDevice();
            throw;
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

    /// <summary>
    /// Cancels generation-owned work, closes the transport, and waits for the
    /// receive loop within the configured shutdown bound.
    /// </summary>
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
        long generation,
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

        if (generation != ConnectionGeneration)
        {
            throw new InvalidOperationException("Connection generation is stale.");
        }

        if (_pendingResponses.Count >= MaximumPendingRequestCount)
        {
            throw new InvalidOperationException(
                "Pending-request capacity has been reached.");
        }

        ushort sequence = NextSequence();
        var completion = new TaskCompletionSource<ProtocolFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRequest = new PendingRequest(
            generation,
            completion);

        if (!_pendingResponses.TryAdd(sequence, pendingRequest))
        {
            throw new InvalidOperationException("Unable to allocate command sequence.");
        }

        using var commandCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        commandCancellation.CancelAfter(timeout);
        CancellationToken commandToken = commandCancellation.Token;

        try
        {
            var request = new ProtocolFrame(
                ProtocolConstants.WireVersion,
                requestType,
                sequence,
                payload);
            byte[] encoded = FrameEncoder.Encode(request);

            await _transport.WriteAsync(encoded, commandToken)
                .ConfigureAwait(false);
            PublishDiagnostic($"TX {requestType} seq={sequence}.");

            ProtocolFrame response = await completion.Task
                .WaitAsync(commandToken)
                .ConfigureAwait(false);
            ValidateResponse(requestType, response, validResponseTypes);
            return response;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested &&
                  commandCancellation.IsCancellationRequested)
        {
            PublishDiagnostic(
                $"Timeout waiting for {requestType} seq={sequence}.");
            throw new TimeoutException(
                $"Command {requestType} timed out after {timeout}.",
                exception);
        }
        finally
        {
            _pendingResponses.TryRemove(sequence, out _);
        }
    }

    private async Task ReceiveLoopAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        byte[] receiveBuffer = new byte[_options.ReceiveBufferSizeBytes];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int receivedLength = await ReadWithPartialFrameTimeoutAsync(
                    receiveBuffer,
                    cancellationToken).ConfigureAwait(false);

                if (receivedLength <= 0)
                {
                    continue;
                }

                _decoder.Append(receiveBuffer.AsSpan(0, receivedLength));

                while (_decoder.TryRead(out ProtocolFrame? frame))
                {
                    if (frame is not null)
                    {
                        HandleFrame(generation, frame);
                    }
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Expected during generation cancellation and orderly shutdown.
        }
        catch (Exception exception)
            when (cancellationToken.IsCancellationRequested ||
                  (State == DeviceSessionState.Disconnecting))
        {
            Debug.WriteLine(exception);
        }
        catch (Exception exception)
        {
            PublishDiagnostic($"Receive loop failed: {exception.Message}");
            FailPendingRequests(generation, exception);

            if (generation == ConnectionGeneration)
            {
                SetState(DeviceSessionState.Faulted);
            }
        }
    }

    private async Task<int> ReadWithPartialFrameTimeoutAsync(
        byte[] receiveBuffer,
        CancellationToken cancellationToken)
    {
        Task<int> readTask = _transport.ReadAsync(
            receiveBuffer,
            cancellationToken).AsTask();

        if (_decoder.BufferedByteCount == 0)
        {
            return await readTask.ConfigureAwait(false);
        }

        Task timeoutTask = Task.Delay(
            _options.PartialFrameTimeout,
            cancellationToken);
        Task completedTask = await Task.WhenAny(readTask, timeoutTask)
            .ConfigureAwait(false);

        if (completedTask == timeoutTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _decoder.DiscardPartialFrame();
            PublishDiagnostic("Discarded partial frame after protocol timeout.");
        }

        return await readTask.ConfigureAwait(false);
    }

    private void HandleFrame(long generation, ProtocolFrame frame)
    {
        if (generation != ConnectionGeneration)
        {
            PublishDiagnostic(
                $"Ignored stale-generation frame seq={frame.Sequence}.");
            return;
        }

        Interlocked.Increment(ref _receivedFrameCount);

        if (frame.Version != ProtocolConstants.WireVersion)
        {
            PublishDiagnostic(
                $"Ignored unsupported protocol version 0x{frame.Version:X2}.");
            return;
        }

        switch (frame.MessageType)
        {
            case MessageType.TelemetrySample:
                HandleTelemetryFrame(frame);
                return;

            case MessageType.DeviceStatus:
                HandleDeviceStatusFrame(frame);
                return;

            case MessageType.ErrorReport:
                HandleErrorReportFrame(frame);
                return;
        }

        if (_pendingResponses.TryGetValue(
                frame.Sequence,
                out PendingRequest? pendingRequest) &&
            (pendingRequest.Generation == generation))
        {
            if (frame.MessageType is MessageType.Ack or MessageType.Nack)
            {
                try
                {
                    CommandResponseStatus responseStatus =
                        PayloadCodec.DecodeCommandResponse(
                            frame.MessageType,
                            frame.Payload.Span);
                    SetDeviceState(responseStatus.DeviceState);
                }
                catch (ProtocolException exception)
                {
                    pendingRequest.Completion.TrySetException(exception);
                    return;
                }
            }

            if (pendingRequest.Completion.TrySetResult(frame))
            {
                RememberCompletedResponse(frame.Sequence);
            }

            return;
        }

        if (MessageTypeValidator.IsDirectResponse(frame.MessageType) &&
            IsRecentResponse(frame.Sequence))
        {
            PublishDiagnostic(
                $"Ignored duplicate response {frame.MessageType} " +
                $"seq={frame.Sequence}.");
            return;
        }

        PublishDiagnostic(
            $"Unmatched RX {frame.MessageType} seq={frame.Sequence}.");
    }

    private void HandleDeviceStatusFrame(ProtocolFrame frame)
    {
        try
        {
            DeviceStatus status = PayloadCodec.DecodeDeviceStatus(frame.Payload.Span);
            SetDeviceState(status.State);
            PublishDeviceStatus(status);
        }
        catch (ProtocolException exception)
        {
            PublishDiagnostic(exception.Message);
        }
    }

    private void HandleErrorReportFrame(ProtocolFrame frame)
    {
        try
        {
            DeviceErrorReport report =
                PayloadCodec.DecodeErrorReport(frame.Payload.Span);
            PublishDeviceErrorReport(report);
        }
        catch (ProtocolException exception)
        {
            PublishDiagnostic(exception.Message);
        }
    }

    private void HandleTelemetryFrame(ProtocolFrame frame)
    {
        if (DeviceState != DeviceOperatingState.Streaming)
        {
            PublishDiagnostic(
                "Ignored TELEMETRY_SAMPLE while the Node is not streaming.");
            return;
        }

        try
        {
            TelemetrySample sample = PayloadCodec.DecodeTelemetry(
                frame.Payload.Span,
                _timeProvider.GetUtcNow());
            UpdateLossCount(sample.SampleCounter);
            PublishTelemetry(sample);
        }
        catch (ProtocolException exception)
        {
            PublishDiagnostic(exception.Message);
        }
    }

    private void UpdateLossCount(uint sampleCounter)
    {
        if (_lastSampleCounter.HasValue)
        {
            uint expected = unchecked(_lastSampleCounter.Value + 1U);
            uint difference = unchecked(sampleCounter - expected);
            const uint MaximumForwardDifference = 0x7FFFFFFF;

            if ((difference > 0U) &&
                (difference <= MaximumForwardDifference))
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
        long retiredGeneration = ConnectionGeneration;

        _receiveCancellation?.Cancel();
        CancelPendingRequests(retiredGeneration);

        Exception? transportFailure = null;
        if (_transport.IsConnected)
        {
            try
            {
                await _transport.DisconnectAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                transportFailure = exception;
                PublishDiagnostic(
                    $"Transport disconnect failed: {exception.Message}");
            }
        }

        Task? receiveTask = _receiveTask;
        if (receiveTask is not null)
        {
            try
            {
                await receiveTask
                    .WaitAsync(
                        _options.ReceiveLoopShutdownTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException exception)
            {
                SetState(DeviceSessionState.Faulted);
                throw new TimeoutException(
                    "Receive loop did not stop within the configured bound.",
                    exception);
            }
        }

        _receiveCancellation?.Dispose();
        _receiveCancellation = null;
        _receiveTask = null;
        DeviceInfo = null;
        _lastSampleCounter = null;
        SetDeviceState(null);

        if (transportFailure is not null)
        {
            SetState(DeviceSessionState.Faulted);
            throw new InvalidOperationException(
                "Transport disconnect did not complete cleanly.",
                transportFailure);
        }

        SetState(DeviceSessionState.Disconnected);
        PublishDiagnostic("Disconnected.");
    }

    private static void ValidateResponse(
        MessageType requestType,
        ProtocolFrame response,
        MessageType[] validResponseTypes)
    {
        if (response.MessageType == MessageType.Nack)
        {
            CommandResponseStatus rejected =
                PayloadCodec.DecodeCommandResponse(
                    response.MessageType,
                    response.Payload.Span);
            throw new DeviceCommandException(
                rejected.RequestType,
                rejected.ResultCode,
                rejected.DeviceState,
                $"Device rejected {rejected.RequestType}: " +
                $"{rejected.ResultCode} while {rejected.DeviceState}.");
        }

        bool isExpectedResponse = false;
        foreach (MessageType validResponseType in validResponseTypes)
        {
            if (response.MessageType == validResponseType)
            {
                isExpectedResponse = true;
                break;
            }
        }

        if (!isExpectedResponse)
        {
            throw new ProtocolException(
                $"Unexpected response {response.MessageType} for {requestType}.");
        }

        if (response.MessageType == MessageType.Ack)
        {
            CommandResponseStatus acknowledged =
                PayloadCodec.DecodeCommandResponse(
                    response.MessageType,
                    response.Payload.Span);

            if (acknowledged.RequestType != requestType)
            {
                throw new ProtocolException(
                    $"ACK does not match request {requestType}.");
            }

            DeviceOperatingState? expectedState = requestType switch
            {
                MessageType.SetStreamConfig => DeviceOperatingState.Idle,
                MessageType.StartStream => DeviceOperatingState.Streaming,
                MessageType.StopStream => DeviceOperatingState.Idle,
                _ => null
            };

            if (expectedState.HasValue &&
                (acknowledged.DeviceState != expectedState.Value))
            {
                throw new ProtocolException(
                    $"ACK state {acknowledged.DeviceState} does not match " +
                    $"the expected state {expectedState.Value} for {requestType}.");
            }
        }
    }

    private void CancelPendingRequests(long generation)
    {
        foreach (PendingRequest pendingRequest in _pendingResponses.Values)
        {
            if (pendingRequest.Generation == generation)
            {
                pendingRequest.Completion.TrySetCanceled();
            }
        }

        _pendingResponses.Clear();
    }

    private void FailPendingRequests(long generation, Exception exception)
    {
        foreach (PendingRequest pendingRequest in _pendingResponses.Values)
        {
            if (pendingRequest.Generation == generation)
            {
                pendingRequest.Completion.TrySetException(exception);
            }
        }
    }

    private void RememberCompletedResponse(ushort sequence)
    {
        lock (_recentResponseGate)
        {
            if (_recentResponseSequences.Add(sequence))
            {
                _recentResponseOrder.Enqueue(sequence);
            }

            while (_recentResponseOrder.Count > RecentResponseCapacity)
            {
                ushort expired = _recentResponseOrder.Dequeue();
                _recentResponseSequences.Remove(expired);
            }
        }
    }

    private bool IsRecentResponse(ushort sequence)
    {
        lock (_recentResponseGate)
        {
            return _recentResponseSequences.Contains(sequence);
        }
    }

    private void ClearRecentResponses()
    {
        lock (_recentResponseGate)
        {
            _recentResponseOrder.Clear();
            _recentResponseSequences.Clear();
        }
    }

    private ushort NextSequence()
    {
        for (int attempt = 0; attempt < ushort.MaxValue; attempt++)
        {
            int value = Interlocked.Increment(ref _sequenceValue);
            ushort sequence = unchecked((ushort)value);

            if ((sequence != 0) && !_pendingResponses.ContainsKey(sequence))
            {
                return sequence;
            }
        }

        throw new InvalidOperationException("No command sequence is available.");
    }

    private static void ValidateStreamInterval(ushort intervalUs)
    {
        if ((intervalUs < ProtocolConstants.MinimumStreamIntervalUs) ||
            (intervalUs > ProtocolConstants.MaximumStreamIntervalUs))
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalUs),
                intervalUs,
                "Stream interval is outside the Project Protocol range.");
        }
    }

    private void EnsureState(params DeviceSessionState[] allowedStates)
    {
        DeviceSessionState currentState = State;
        foreach (DeviceSessionState allowedState in allowedStates)
        {
            if (currentState == allowedState)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Operation is not allowed while the session is {currentState}.");
    }

    private void SynchronizeSessionStateFromDevice()
    {
        DeviceSessionState sessionState = DeviceState switch
        {
            DeviceOperatingState.Idle => DeviceSessionState.Ready,
            DeviceOperatingState.Streaming => DeviceSessionState.Streaming,
            _ => DeviceSessionState.Faulted
        };
        SetState(sessionState);
    }

    private void SetDeviceState(DeviceOperatingState? state)
    {
        int newValue = state.HasValue ? (int)state.Value : -1;
        int previousValue = Interlocked.Exchange(
            ref _deviceStateValue,
            newValue);

        if (state.HasValue && (previousValue != newValue))
        {
            PublishDeviceOperatingStateChanged(state.Value);
        }
    }

    private void SetState(DeviceSessionState state)
    {
        DeviceSessionState previous =
            (DeviceSessionState)Interlocked.Exchange(
                ref _stateValue,
                (int)state);

        if (previous != state)
        {
            PublishStateChanged(state);
        }
    }

    private void PublishDeviceOperatingStateChanged(DeviceOperatingState state)
    {
        Action<DeviceOperatingState>? handlers = DeviceOperatingStateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<DeviceOperatingState>)subscriber)(state);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        }
    }

    private void PublishDeviceStatus(DeviceStatus status)
    {
        Action<DeviceStatus>? handlers = DeviceStatusReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<DeviceStatus>)subscriber)(status);
            }
            catch (Exception exception)
            {
                PublishDiagnostic(
                    $"Device-status subscriber failed: {exception.Message}");
            }
        }
    }

    private void PublishDeviceErrorReport(DeviceErrorReport report)
    {
        Action<DeviceErrorReport>? handlers = DeviceErrorReported;
        if (handlers is null)
        {
            PublishDiagnostic(
                $"Device error 0x{report.ErrorCode:X4}, detail=0x{report.Detail:X8}.");
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<DeviceErrorReport>)subscriber)(report);
            }
            catch (Exception exception)
            {
                PublishDiagnostic(
                    $"Device-error subscriber failed: {exception.Message}");
            }
        }
    }

    private void PublishStateChanged(DeviceSessionState state)
    {
        Action<DeviceSessionState>? handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<DeviceSessionState>)subscriber)(state);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        }
    }

    private void PublishTelemetry(TelemetrySample sample)
    {
        Action<TelemetrySample>? handlers = TelemetryReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<TelemetrySample>)subscriber)(sample);
            }
            catch (Exception exception)
            {
                PublishDiagnostic(
                    $"Telemetry subscriber failed: {exception.Message}");
            }
        }
    }

    private void PublishDiagnostic(string message)
    {
        string sanitizedMessage = DiagnosticText.Sanitize(message);
        Action<string>? handlers = DiagnosticMessage;
        if (handlers is null)
        {
            Debug.WriteLine(sanitizedMessage);
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<string>)subscriber)(sanitizedMessage);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Performs deterministic asynchronous shutdown and releases the owned
    /// transport and lifecycle gate.
    /// </summary>
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

    private sealed record PendingRequest(
        long Generation,
        TaskCompletionSource<ProtocolFrame> Completion);
}
