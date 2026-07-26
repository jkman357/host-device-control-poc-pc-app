// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HostDeviceControl.App.Infrastructure;
using HostDeviceControl.App.Services;
using HostDeviceControl.Core.Abstractions;
using HostDeviceControl.Core.Concurrency;
using HostDeviceControl.Core.Device;
using HostDeviceControl.Core.Diagnostics;
using HostDeviceControl.Core.Models;
using HostDeviceControl.Core.Protocol;
using HostDeviceControl.Transport.Fake;
using HostDeviceControl.Transport.Serial;
using Microsoft.Win32;

namespace HostDeviceControl.App.ViewModels;

/// <summary>
/// Owns presentation state for the single-node PoC window. Device and protocol
/// state remain authoritative in <see cref="DeviceSession"/>.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const string FakeConnectionMode = "Fake Device";
    private const string SerialConnectionMode = "Serial Port";
    private const string RecordingStoppedText = "Stopped";
    private const int MaximumChartSamples = 2000;
    private const int MaximumLogEntries = 500;
    private const int UiTelemetryCapacity = 2048;
    private const int MaximumSamplesPerUiTick = 512;
    private const int DiagnosticCapacity = 256;
    private const int MaximumDiagnosticsPerUiTick = 64;
    private const int UiRefreshIntervalMilliseconds = 50;

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly Dispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _applicationCancellation = new();
    private readonly BoundedDropOldestBuffer<TelemetrySample> _telemetryBuffer =
        new(UiTelemetryCapacity);
    private readonly List<TelemetrySample> _uiDrainBuffer =
        new(MaximumSamplesPerUiTick);
    private readonly BoundedDropOldestBuffer<string> _diagnosticBuffer =
        new(DiagnosticCapacity);
    private readonly List<string> _diagnosticDrainBuffer =
        new(MaximumDiagnosticsPerUiTick);
    private readonly List<double> _chartHistory = new(MaximumChartSamples);
    private readonly DispatcherTimer _uiTimer;
    private readonly CsvTelemetryRecorder _recorder = new();

    private DeviceSession? _session;
    private string _selectedConnectionMode = FakeConnectionMode;
    private string? _selectedPortName;
    private int _selectedBaudRate = SerialTransportOptions.DefaultBaudRate;
    private string _sessionState = DeviceSessionState.Disconnected.ToString();
    private string _deviceSummary = "Not connected";
    private string _statusMessage =
        "Ready. Use Fake Device to run without hardware.";
    private string _recordingStatus = RecordingStoppedText;
    private IReadOnlyList<double> _chartSamples = Array.Empty<double>();
    private long _receivedFrameCount;
    private long _receivedSampleCount;
    private long _receivedSampleCounter;
    private long _crcErrorCount;
    private long _formatErrorCount;
    private long _unknownMessageTypeCount;
    private long _partialFrameTimeoutCount;
    private long _lostSampleCount;
    private long _uiDropCount;
    private int _uiQueueDepth;
    private long _recorderDropCount;
    private long _diagnosticDropCount;
    private uint _latestDeviceTickUs;
    private bool _isShuttingDown;
    private bool _disposed;

    /// <summary>
    /// Initializes the presentation model with an explicit UI dispatcher and
    /// optional time provider for deterministic tests.
    /// </summary>
    public MainViewModel(
        Dispatcher dispatcher,
        TimeProvider? timeProvider = null)
    {
        _dispatcher = dispatcher ??
            throw new ArgumentNullException(nameof(dispatcher));
        _timeProvider = timeProvider ?? TimeProvider.System;

        ConnectionModes = [FakeConnectionMode, SerialConnectionMode];
        BaudRates = SerialTransportOptions.SupportedBaudRates;
        PortNames = new ObservableCollection<string>();
        LogEntries = new ObservableCollection<string>();

        RefreshPortsCommand = new RelayCommand(
            RefreshPorts,
            () => IsConnectionConfigurationEditable && IsSerialMode);
        ConnectCommand = CreateAsyncCommand(
            ConnectAsync,
            () => !_isShuttingDown &&
                  (_session is null ||
                   _session.State == DeviceSessionState.Disconnected));
        DisconnectCommand = CreateAsyncCommand(
            DisconnectAsync,
            () => !_isShuttingDown &&
                  _session is not null &&
                  _session.State != DeviceSessionState.Disconnected);
        StartStreamCommand = CreateAsyncCommand(
            StartStreamingAsync,
            CanStartStreaming);
        StopStreamCommand = CreateAsyncCommand(
            StopStreamingAsync,
            () => !_isShuttingDown &&
                  _session?.State == DeviceSessionState.Streaming);
        ClearChartCommand = new RelayCommand(ClearChart);
        StartRecordingCommand = CreateAsyncCommand(
            StartRecordingAsync,
            () => !_isShuttingDown && !_recorder.IsRecording);
        StopRecordingCommand = CreateAsyncCommand(
            StopRecordingAsync,
            () => !_isShuttingDown && _recorder.IsRecording);

        _recorder.OverrunDetected += OnRecorderOverrunDetected;

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(UiRefreshIntervalMilliseconds)
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();

        RefreshPorts();
        AddLog("Application initialized.");
    }

    public IReadOnlyList<string> ConnectionModes { get; }

    public IReadOnlyList<int> BaudRates { get; }

    public ObservableCollection<string> PortNames { get; }

    public ObservableCollection<string> LogEntries { get; }

    public RelayCommand RefreshPortsCommand { get; }

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand StartStreamCommand { get; }

    public AsyncRelayCommand StopStreamCommand { get; }

    public RelayCommand ClearChartCommand { get; }

    public AsyncRelayCommand StartRecordingCommand { get; }

    public AsyncRelayCommand StopRecordingCommand { get; }

    public string SelectedConnectionMode
    {
        get => _selectedConnectionMode;
        set
        {
            if (SetProperty(ref _selectedConnectionMode, value))
            {
                OnPropertyChanged(nameof(IsSerialMode));
                OnPropertyChanged(nameof(IsSerialConfigurationEditable));
                OnPropertyChanged(nameof(StreamCapabilitySummary));
                RaiseCommandStates();
            }
        }
    }

    public bool IsSerialMode =>
        string.Equals(
            SelectedConnectionMode,
            SerialConnectionMode,
            StringComparison.Ordinal);

    public bool IsConnectionConfigurationEditable =>
        !_isShuttingDown &&
        (_session is null ||
         _session.State == DeviceSessionState.Disconnected);

    public bool IsSerialConfigurationEditable =>
        IsSerialMode && IsConnectionConfigurationEditable;

    public string? SelectedPortName
    {
        get => _selectedPortName;
        set => SetProperty(ref _selectedPortName, value);
    }

    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set
        {
            if (SetProperty(ref _selectedBaudRate, value))
            {
                OnPropertyChanged(nameof(StreamCapabilitySummary));
                RaiseCommandStates();
            }
        }
    }

    public string StreamCapabilitySummary
    {
        get
        {
            if (!IsSerialMode)
            {
                return "Stream: 200 Hz";
            }

            if (!SerialStreamCapacity.TrySelectStreamIntervalUs(
                    SelectedBaudRate,
                    ProtocolConstants.DefaultStreamIntervalUs,
                    out ushort intervalUs))
            {
                return "Stream: command-only";
            }

            double frequencyHz = 1_000_000.0 / intervalUs;
            return $"Stream: {frequencyHz:0.##} Hz max";
        }
    }

    public string SessionState
    {
        get => _sessionState;
        private set => SetProperty(ref _sessionState, value);
    }

    public string DeviceSummary
    {
        get => _deviceSummary;
        private set => SetProperty(ref _deviceSummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string RecordingStatus
    {
        get => _recordingStatus;
        private set => SetProperty(ref _recordingStatus, value);
    }

    public IReadOnlyList<double> ChartSamples
    {
        get => _chartSamples;
        private set => SetProperty(ref _chartSamples, value);
    }

    public long ReceivedFrameCount
    {
        get => _receivedFrameCount;
        private set => SetProperty(ref _receivedFrameCount, value);
    }

    public long ReceivedSampleCount
    {
        get => _receivedSampleCount;
        private set => SetProperty(ref _receivedSampleCount, value);
    }

    public long CrcErrorCount
    {
        get => _crcErrorCount;
        private set => SetProperty(ref _crcErrorCount, value);
    }

    public long FormatErrorCount
    {
        get => _formatErrorCount;
        private set => SetProperty(ref _formatErrorCount, value);
    }

    public long UnknownMessageTypeCount
    {
        get => _unknownMessageTypeCount;
        private set => SetProperty(ref _unknownMessageTypeCount, value);
    }

    public long PartialFrameTimeoutCount
    {
        get => _partialFrameTimeoutCount;
        private set => SetProperty(ref _partialFrameTimeoutCount, value);
    }

    public long LostSampleCount
    {
        get => _lostSampleCount;
        private set => SetProperty(ref _lostSampleCount, value);
    }

    public long UiDropCount
    {
        get => _uiDropCount;
        private set => SetProperty(ref _uiDropCount, value);
    }

    public int UiQueueDepth
    {
        get => _uiQueueDepth;
        private set => SetProperty(ref _uiQueueDepth, value);
    }

    public long RecorderDropCount
    {
        get => _recorderDropCount;
        private set => SetProperty(ref _recorderDropCount, value);
    }

    public long DiagnosticDropCount
    {
        get => _diagnosticDropCount;
        private set => SetProperty(ref _diagnosticDropCount, value);
    }

    public uint LatestDeviceTickUs
    {
        get => _latestDeviceTickUs;
        private set => SetProperty(ref _latestDeviceTickUs, value);
    }

    private AsyncRelayCommand CreateAsyncCommand(
        Func<Task> executeAsync,
        Func<bool> canExecute)
    {
        return new AsyncRelayCommand(
            executeAsync,
            HandleUnexpectedCommandException,
            canExecute);
    }

    private void RefreshPorts()
    {
        string? previousSelection = SelectedPortName;
        string[] names;

        try
        {
            names = SerialPortDiscovery.GetPortNames();
        }
        catch (Exception exception)
        {
            AddLog($"Port discovery failed: {exception.Message}");
            names = [];
        }

        PortNames.Clear();
        foreach (string name in names)
        {
            PortNames.Add(name);
        }

        SelectedPortName =
            (previousSelection is not null) && names.Contains(previousSelection)
                ? previousSelection
                : names.FirstOrDefault();
    }

    private async Task ConnectAsync()
    {
        CancellationToken cancellationToken = _applicationCancellation.Token;
        IDeviceTransport? transport = null;
        DeviceSession? session = null;

        try
        {
            transport = CreateTransport();
            session = new DeviceSession(
                transport,
                DeviceSessionOptions.Default,
                _timeProvider);
            transport = null;
            SubscribeSession(session);
            _session = session;
            NotifySessionOwnershipChanged();

            string connectionDescription = IsSerialMode
                ? $"{SelectedConnectionMode} " +
                  $"({SelectedPortName}, {SelectedBaudRate} baud)"
                : SelectedConnectionMode;
            StatusMessage = $"Connecting through {connectionDescription}...";
            await session.ConnectAsync(cancellationToken);

            UpdateDeviceSummary();
            StatusMessage = IsSerialMode
                ? $"Connected. Device state: {session.DeviceState}. " +
                  $"{StreamCapabilitySummary}."
                : $"Connected. Device state: {session.DeviceState}.";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Connection cancelled.";
            await CleanupFailedConnectionAsync(session, transport);
            throw;
        }
        catch (Exception exception)
        {
            AddLog($"Connect failed: {exception.Message}");
            StatusMessage = "Connection failed.";
            await CleanupFailedConnectionAsync(session, transport);
        }
    }

    private IDeviceTransport CreateTransport()
    {
        if (!IsSerialMode)
        {
            return new FakeDeviceTransport();
        }

        if (string.IsNullOrWhiteSpace(SelectedPortName))
        {
            throw new InvalidOperationException("Select a COM port first.");
        }

        return new SerialDeviceTransport(
            new SerialTransportOptions(
                SelectedPortName,
                SelectedBaudRate));
    }

    private Task DisconnectAsync()
    {
        return DisconnectCoreAsync(_applicationCancellation.Token);
    }

    private async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        DeviceSession? session = _session;
        if (session is null)
        {
            return;
        }

        try
        {
            if (session.State == DeviceSessionState.Streaming)
            {
                await session.StopStreamingAsync(cancellationToken);
            }

            await session.DisconnectAsync(cancellationToken);
            await session.DisposeAsync();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddLog($"Disconnect incomplete: {exception.Message}");
            StatusMessage =
                "Disconnect did not complete cleanly; retry or close the app.";
            SessionState = session.State.ToString();
            RaiseCommandStates();
            return;
        }

        UnsubscribeSession(session);
        if (ReferenceEquals(_session, session))
        {
            _session = null;
        }

        SessionState = DeviceSessionState.Disconnected.ToString();
        DeviceSummary = "Not connected";
        StatusMessage = "Disconnected.";
        NotifySessionOwnershipChanged();
    }

    private async Task StartStreamingAsync()
    {
        DeviceSession session = _session ??
            throw new InvalidOperationException("Device is not connected.");

        try
        {
            ushort intervalUs = GetSelectedStreamIntervalUs();
            await session.StartStreamingAsync(
                intervalUs,
                _applicationCancellation.Token);
            double frequencyHz = 1_000_000.0 / intervalUs;
            StatusMessage =
                $"Receiving {frequencyHz:0.##} Hz telemetry " +
                $"({intervalUs} us interval).";
        }
        catch (OperationCanceledException)
            when (_applicationCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddLog($"Start stream failed: {exception.Message}");
            StatusMessage = "Unable to start streaming.";
        }
    }

    private bool CanStartStreaming()
    {
        return !_isShuttingDown &&
               _session?.State == DeviceSessionState.Ready &&
               IsSelectedStreamConfigurationSupported();
    }

    private bool IsSelectedStreamConfigurationSupported()
    {
        return !IsSerialMode ||
               SerialStreamCapacity.TrySelectStreamIntervalUs(
                   SelectedBaudRate,
                   ProtocolConstants.DefaultStreamIntervalUs,
                   out _);
    }

    private ushort GetSelectedStreamIntervalUs()
    {
        if (!IsSerialMode)
        {
            return ProtocolConstants.DefaultStreamIntervalUs;
        }

        if (SerialStreamCapacity.TrySelectStreamIntervalUs(
                SelectedBaudRate,
                ProtocolConstants.DefaultStreamIntervalUs,
                out ushort intervalUs))
        {
            return intervalUs;
        }

        throw new InvalidOperationException(
            $"{SelectedBaudRate} baud is command-only under the configured " +
            "UART capacity policy.");
    }

    private async Task StopStreamingAsync()
    {
        DeviceSession session = _session ??
            throw new InvalidOperationException("Device is not connected.");

        try
        {
            await session.StopStreamingAsync(_applicationCancellation.Token);
            StatusMessage = "Streaming stopped.";
        }
        catch (OperationCanceledException)
            when (_applicationCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddLog($"Stop stream failed: {exception.Message}");
            StatusMessage = "Unable to stop streaming cleanly.";
        }
    }

    private Task StartRecordingAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save telemetry CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName =
                $"telemetry-{_timeProvider.GetLocalNow():yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        return StartRecorderCoreAsync(dialog.FileName);
    }

    private async Task StartRecorderCoreAsync(string filePath)
    {
        try
        {
            await _recorder.StartAsync(
                filePath,
                _applicationCancellation.Token);
            RecordingStatus = Path.GetFileName(filePath);
            StatusMessage = "Telemetry recording started.";
            AddLog($"Recording started: {Path.GetFileName(filePath)}.");
        }
        catch (OperationCanceledException)
            when (_applicationCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddLog($"Unable to start recording: {exception.Message}");
            StatusMessage = "Recording start failed.";
        }
        finally
        {
            RaiseCommandStates();
        }
    }

    private async Task StopRecordingAsync()
    {
        try
        {
            await _recorder.StopAsync(_applicationCancellation.Token);
            RecordingStatus = RecordingStoppedText;
            StatusMessage = "Telemetry recording stopped.";
            AddLog("Recording stopped.");
        }
        catch (OperationCanceledException)
            when (_applicationCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddLog($"Unable to stop recording: {exception.Message}");
        }
        finally
        {
            RaiseCommandStates();
        }
    }

    private void ClearChart()
    {
        _chartHistory.Clear();
        _telemetryBuffer.Clear();
        ChartSamples = Array.Empty<double>();
        UiQueueDepth = 0;
        StatusMessage = "Waveform cleared.";
    }

    private void SubscribeSession(DeviceSession session)
    {
        session.StateChanged += OnSessionStateChanged;
        session.DeviceOperatingStateChanged += OnDeviceOperatingStateChanged;
        session.DeviceStatusReceived += OnDeviceStatusReceived;
        session.DeviceErrorReported += OnDeviceErrorReported;
        session.TelemetryReceived += OnTelemetryReceived;
        session.DiagnosticMessage += OnDiagnosticMessage;
    }

    private void UnsubscribeSession(DeviceSession session)
    {
        session.StateChanged -= OnSessionStateChanged;
        session.DeviceOperatingStateChanged -= OnDeviceOperatingStateChanged;
        session.DeviceStatusReceived -= OnDeviceStatusReceived;
        session.DeviceErrorReported -= OnDeviceErrorReported;
        session.TelemetryReceived -= OnTelemetryReceived;
        session.DiagnosticMessage -= OnDiagnosticMessage;
    }

    private void OnSessionStateChanged(DeviceSessionState state)
    {
        PostToUi(() =>
        {
            SessionState = state.ToString();
            OnPropertyChanged(nameof(IsConnectionConfigurationEditable));
            OnPropertyChanged(nameof(IsSerialConfigurationEditable));
            RaiseCommandStates();
        });
    }

    private void OnDeviceOperatingStateChanged(DeviceOperatingState state)
    {
        PostToUi(UpdateDeviceSummary);
    }

    private void OnDeviceStatusReceived(DeviceStatus status)
    {
        _diagnosticBuffer.Enqueue(
            $"DEVICE_STATUS state={status.State}, flags=0x{(ushort)status.StatusBits:X4}.");
    }

    private void OnDeviceErrorReported(DeviceErrorReport report)
    {
        _diagnosticBuffer.Enqueue(
            $"ERROR_REPORT code=0x{report.ErrorCode:X4}, " +
            $"detail=0x{report.Detail:X8}.");
    }

    private void UpdateDeviceSummary()
    {
        DeviceSession? session = _session;
        DeviceInfo? info = session?.DeviceInfo;
        if (session is null || info is null)
        {
            DeviceSummary = session is null ? "Not connected" : "Connected";
            return;
        }

        string deviceState = session.DeviceState?.ToString() ?? "Unknown";
        DeviceSummary =
            $"{info.DeviceName} | FW {info.FirmwareVersion} | " +
            $"Type 0x{info.DeviceType:X4} | State {deviceState}";
    }

    private void OnTelemetryReceived(TelemetrySample sample)
    {
        _telemetryBuffer.Enqueue(sample);
        Interlocked.Increment(ref _receivedSampleCounter);

        if (_recorder.IsRecording)
        {
            _recorder.TryRecord(sample);
        }
    }

    private void OnDiagnosticMessage(string message)
    {
        _diagnosticBuffer.Enqueue(DiagnosticText.Sanitize(message));
    }

    private void OnRecorderOverrunDetected(long droppedSampleCount)
    {
        PostToUi(() =>
        {
            AddLog(
                $"Recording queue overrun detected; dropped samples: " +
                $"{droppedSampleCount}.");
            StatusMessage =
                "Recording is incomplete because the storage queue overran.";
        });
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        _diagnosticDrainBuffer.Clear();
        _diagnosticBuffer.DrainTo(
            _diagnosticDrainBuffer,
            MaximumDiagnosticsPerUiTick);
        foreach (string diagnostic in _diagnosticDrainBuffer)
        {
            AddLog(diagnostic);
        }

        DiagnosticDropCount = _diagnosticBuffer.DroppedItemCount;

        _uiDrainBuffer.Clear();
        int drainedSampleCount = _telemetryBuffer.DrainTo(
            _uiDrainBuffer,
            MaximumSamplesPerUiTick);

        if (drainedSampleCount > 0)
        {
            foreach (TelemetrySample sample in _uiDrainBuffer)
            {
                _chartHistory.Add(sample.SineValue);
                LatestDeviceTickUs = sample.DeviceTickUs;
            }

            if (_chartHistory.Count > MaximumChartSamples)
            {
                int removeCount = _chartHistory.Count - MaximumChartSamples;
                _chartHistory.RemoveRange(0, removeCount);
            }

            ChartSamples = _chartHistory.ToArray();
        }

        DeviceSession? session = _session;
        if (session is not null)
        {
            ReceivedFrameCount = session.ReceivedFrameCount;
            CrcErrorCount = session.CrcErrorCount;
            FormatErrorCount = session.FormatErrorCount;
            UnknownMessageTypeCount = session.UnknownMessageTypeCount;
            PartialFrameTimeoutCount = session.PartialFrameTimeoutCount;
            LostSampleCount = session.LostSampleCount;
        }

        ReceivedSampleCount = Interlocked.Read(ref _receivedSampleCounter);
        UiDropCount = _telemetryBuffer.DroppedItemCount;
        UiQueueDepth = _telemetryBuffer.Count;
        RecorderDropCount = _recorder.DroppedSampleCount;
    }

    private async Task CleanupFailedConnectionAsync(
        DeviceSession? session,
        IDeviceTransport? transport)
    {
        bool cleanupCompleted = true;

        try
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            else if (transport is not null)
            {
                await transport.DisposeAsync();
            }
        }
        catch (Exception cleanupException)
        {
            cleanupCompleted = false;
            AddLog($"Connection cleanup incomplete: {cleanupException.Message}");
            StatusMessage =
                "Connection failed and cleanup is incomplete; retry disconnect.";
        }

        if (cleanupCompleted && (session is not null))
        {
            UnsubscribeSession(session);
        }

        if (cleanupCompleted && ReferenceEquals(_session, session))
        {
            _session = null;
        }

        SessionState = cleanupCompleted
            ? DeviceSessionState.Disconnected.ToString()
            : session?.State.ToString() ?? DeviceSessionState.Faulted.ToString();
        DeviceSummary = "Not connected";
        NotifySessionOwnershipChanged();
    }

    private void AddLog(string message)
    {
        string sanitizedMessage = DiagnosticText.Sanitize(message);
        DateTimeOffset localTime = _timeProvider.GetLocalNow();
        string entry = $"{localTime:HH:mm:ss.fff}  {sanitizedMessage}";
        LogEntries.Add(entry);

        while (LogEntries.Count > MaximumLogEntries)
        {
            LogEntries.RemoveAt(0);
        }
    }

    private void HandleUnexpectedCommandException(Exception exception)
    {
        PostToUi(() =>
        {
            AddLog($"Unexpected command failure: {exception.Message}");
            StatusMessage = "An unexpected operation failure occurred.";
        });
    }

    private void PostToUi(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            ExecuteUiAction(action);
            return;
        }

        _dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => ExecuteUiAction(action)));
    }

    private void ExecuteUiAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusMessage = "A presentation update failed.";
        }
    }

    private void NotifySessionOwnershipChanged()
    {
        OnPropertyChanged(nameof(IsConnectionConfigurationEditable));
        OnPropertyChanged(nameof(IsSerialConfigurationEditable));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        RefreshPortsCommand.RaiseCanExecuteChanged();
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        StartStreamCommand.RaiseCanExecuteChanged();
        StopStreamCommand.RaiseCanExecuteChanged();
        StartRecordingCommand.RaiseCanExecuteChanged();
        StopRecordingCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Cancels application work and performs a bounded, retryable shutdown. A
    /// timeout is surfaced to the window so incomplete shutdown is not hidden.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _isShuttingDown = true;
        NotifySessionOwnershipChanged();
        _uiTimer.Stop();
        _uiTimer.Tick -= OnUiTimerTick;
        _applicationCancellation.Cancel();

        using var shutdownCancellation = new CancellationTokenSource(
            ShutdownTimeout);
        CancellationToken shutdownToken = shutdownCancellation.Token;

        DeviceSession? session = _session;
        if (session is not null)
        {
            try
            {
                if (session.State == DeviceSessionState.Streaming)
                {
                    await session.StopStreamingAsync(shutdownToken);
                }
            }
            catch (OperationCanceledException)
                when (shutdownToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AddLog($"Stop during shutdown failed: {exception.Message}");
            }
        }

        if (_recorder.IsRecording)
        {
            await _recorder.StopAsync(shutdownToken);
        }

        if (session is not null)
        {
            await session.DisconnectAsync(shutdownToken);
            await session.DisposeAsync();
            UnsubscribeSession(session);
            _session = null;
        }

        _recorder.OverrunDetected -= OnRecorderOverrunDetected;
        await _recorder.DisposeAsync();
        _applicationCancellation.Dispose();
        _disposed = true;
    }
}
