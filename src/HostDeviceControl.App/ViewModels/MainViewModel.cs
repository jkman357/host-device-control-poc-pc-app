using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HostDeviceControl.App.Infrastructure;
using HostDeviceControl.App.Services;
using HostDeviceControl.Core.Abstractions;
using HostDeviceControl.Core.Device;
using HostDeviceControl.Core.Models;
using HostDeviceControl.Core.Protocol;
using HostDeviceControl.Transport.Fake;
using HostDeviceControl.Transport.Serial;
using Microsoft.Win32;

namespace HostDeviceControl.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const string FakeConnectionMode = "Fake Device";
    private const string SerialConnectionMode = "Serial Port";
    private const int MaximumChartSamples = 2000;
    private const int MaximumLogEntries = 500;

    private readonly ConcurrentQueue<TelemetrySample> _telemetryQueue = new();
    private readonly List<double> _chartHistory = new(MaximumChartSamples);
    private readonly DispatcherTimer _uiTimer;
    private readonly CsvTelemetryRecorder _recorder = new();

    private DeviceSession? _session;
    private string _selectedConnectionMode = FakeConnectionMode;
    private string? _selectedPortName;
    private string _baudRate = "115200";
    private string _sessionState = DeviceSessionState.Disconnected.ToString();
    private string _deviceSummary = "Not connected";
    private string _statusMessage = "Ready. Use Fake Device to run without hardware.";
    private string _recordingStatus = "Stopped";
    private IReadOnlyList<double> _chartSamples = Array.Empty<double>();
    private long _receivedFrameCount;
    private long _receivedSampleCount;
    private long _receivedSampleCounter;
    private long _crcErrorCount;
    private long _lostSampleCount;
    private long _recorderDropCount;
    private uint _latestDeviceTickUs;
    private bool _disposed;

    public MainViewModel()
    {
        ConnectionModes = [FakeConnectionMode, SerialConnectionMode];
        PortNames = new ObservableCollection<string>();
        LogEntries = new ObservableCollection<string>();

        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        ConnectCommand = new AsyncRelayCommand(
            ConnectAsync,
            () => _session is null ||
                  _session.State == DeviceSessionState.Disconnected);
        DisconnectCommand = new AsyncRelayCommand(
            DisconnectAsync,
            () => _session is not null &&
                  _session.State != DeviceSessionState.Disconnected);
        StartStreamCommand = new AsyncRelayCommand(
            StartStreamingAsync,
            () => _session?.State == DeviceSessionState.Ready);
        StopStreamCommand = new AsyncRelayCommand(
            StopStreamingAsync,
            () => _session?.State == DeviceSessionState.Streaming);
        ClearChartCommand = new RelayCommand(ClearChart);
        StartRecordingCommand = new AsyncRelayCommand(
            StartRecordingAsync,
            () => !_recorder.IsRecording);
        StopRecordingCommand = new AsyncRelayCommand(
            StopRecordingAsync,
            () => _recorder.IsRecording);

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();

        RefreshPorts();
        AddLog("Application initialized.");
    }

    public IReadOnlyList<string> ConnectionModes { get; }

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
                RaiseCommandStates();
            }
        }
    }

    public bool IsSerialMode =>
        string.Equals(
            SelectedConnectionMode,
            SerialConnectionMode,
            StringComparison.Ordinal);

    public string? SelectedPortName
    {
        get => _selectedPortName;
        set => SetProperty(ref _selectedPortName, value);
    }

    public string BaudRate
    {
        get => _baudRate;
        set => SetProperty(ref _baudRate, value);
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

    public long LostSampleCount
    {
        get => _lostSampleCount;
        private set => SetProperty(ref _lostSampleCount, value);
    }

    public long RecorderDropCount
    {
        get => _recorderDropCount;
        private set => SetProperty(ref _recorderDropCount, value);
    }

    public uint LatestDeviceTickUs
    {
        get => _latestDeviceTickUs;
        private set => SetProperty(ref _latestDeviceTickUs, value);
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
        try
        {
            IDeviceTransport transport = CreateTransport();
            var session = new DeviceSession(transport);
            SubscribeSession(session);
            _session = session;
            RaiseCommandStates();

            StatusMessage = $"Connecting through {SelectedConnectionMode}...";
            await session.ConnectAsync(CancellationToken.None);

            DeviceInfo? info = session.DeviceInfo;
            DeviceSummary = info is null
                ? "Connected"
                : $"{info.DeviceName} | FW {info.FirmwareVersion} | " +
                  $"Type 0x{info.DeviceType:X4}";
            StatusMessage = "Connected and ready.";
        }
        catch (Exception exception)
        {
            AddLog($"Connect failed: {exception.Message}");
            StatusMessage = "Connection failed.";

            if (_session is not null)
            {
                UnsubscribeSession(_session);
                await _session.DisposeAsync();
                _session = null;
            }

            SessionState = DeviceSessionState.Disconnected.ToString();
            DeviceSummary = "Not connected";
            RaiseCommandStates();
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

        if (!int.TryParse(
                BaudRate,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int baudRate) ||
            baudRate <= 0)
        {
            throw new InvalidOperationException("Baud rate must be a positive integer.");
        }

        return new SerialDeviceTransport(
            new SerialTransportOptions(SelectedPortName, baudRate));
    }

    private async Task DisconnectAsync()
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
                await session.StopStreamingAsync(CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            AddLog($"Stop during disconnect failed: {exception.Message}");
        }

        try
        {
            await session.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            AddLog($"Disconnect failed: {exception.Message}");
        }
        finally
        {
            UnsubscribeSession(session);
            await session.DisposeAsync();
            _session = null;
            SessionState = DeviceSessionState.Disconnected.ToString();
            DeviceSummary = "Not connected";
            StatusMessage = "Disconnected.";
            RaiseCommandStates();
        }
    }

    private async Task StartStreamingAsync()
    {
        DeviceSession session = _session ??
            throw new InvalidOperationException("Device is not connected.");

        try
        {
            await session.StartStreamingAsync(
                ProtocolConstants.DefaultStreamIntervalUs,
                CancellationToken.None);
            StatusMessage = "Receiving 200 Hz telemetry.";
        }
        catch (Exception exception)
        {
            AddLog($"Start stream failed: {exception.Message}");
            StatusMessage = "Unable to start streaming.";
        }
    }

    private async Task StopStreamingAsync()
    {
        DeviceSession session = _session ??
            throw new InvalidOperationException("Device is not connected.");

        try
        {
            await session.StopStreamingAsync(CancellationToken.None);
            StatusMessage = "Streaming stopped.";
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
            FileName = $"telemetry-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
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
            await _recorder.StartAsync(filePath, CancellationToken.None);
            RecordingStatus = filePath;
            StatusMessage = "Telemetry recording started.";
            AddLog($"Recording to {filePath}.");
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
            await _recorder.StopAsync();
            RecordingStatus = "Stopped";
            StatusMessage = "Telemetry recording stopped.";
            AddLog("Recording stopped.");
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
        while (_telemetryQueue.TryDequeue(out _))
        {
        }

        ChartSamples = Array.Empty<double>();
        StatusMessage = "Waveform cleared.";
    }

    private void SubscribeSession(DeviceSession session)
    {
        session.StateChanged += OnSessionStateChanged;
        session.TelemetryReceived += OnTelemetryReceived;
        session.DiagnosticMessage += OnDiagnosticMessage;
    }

    private void UnsubscribeSession(DeviceSession session)
    {
        session.StateChanged -= OnSessionStateChanged;
        session.TelemetryReceived -= OnTelemetryReceived;
        session.DiagnosticMessage -= OnDiagnosticMessage;
    }

    private void OnSessionStateChanged(DeviceSessionState state)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            SessionState = state.ToString();
            RaiseCommandStates();
        });
    }

    private void OnTelemetryReceived(TelemetrySample sample)
    {
        _telemetryQueue.Enqueue(sample);
        Interlocked.Increment(ref _receivedSampleCounter);

        if (_recorder.IsRecording)
        {
            _recorder.TryRecord(sample);
        }
    }

    private void OnDiagnosticMessage(string message)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() => AddLog(message));
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        bool chartChanged = false;

        while (_telemetryQueue.TryDequeue(out TelemetrySample sample))
        {
            _chartHistory.Add(sample.SineValue);
            LatestDeviceTickUs = sample.DeviceTickUs;
            chartChanged = true;
        }

        if (_chartHistory.Count > MaximumChartSamples)
        {
            int removeCount = _chartHistory.Count - MaximumChartSamples;
            _chartHistory.RemoveRange(0, removeCount);
            chartChanged = true;
        }

        if (chartChanged)
        {
            ChartSamples = _chartHistory.ToArray();
        }

        DeviceSession? session = _session;
        if (session is not null)
        {
            ReceivedFrameCount = session.ReceivedFrameCount;
            CrcErrorCount = session.CrcErrorCount;
            LostSampleCount = session.LostSampleCount;
        }

        ReceivedSampleCount = Interlocked.Read(ref _receivedSampleCounter);
        RecorderDropCount = _recorder.DroppedSampleCount;
    }

    private void AddLog(string message)
    {
        string entry = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        LogEntries.Add(entry);

        while (LogEntries.Count > MaximumLogEntries)
        {
            LogEntries.RemoveAt(0);
        }
    }

    private void RaiseCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        StartStreamCommand.RaiseCanExecuteChanged();
        StopStreamCommand.RaiseCanExecuteChanged();
        StartRecordingCommand.RaiseCanExecuteChanged();
        StopRecordingCommand.RaiseCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _uiTimer.Stop();
        _uiTimer.Tick -= OnUiTimerTick;

        await _recorder.DisposeAsync();

        if (_session is not null)
        {
            UnsubscribeSession(_session);
            await _session.DisposeAsync();
            _session = null;
        }

        _disposed = true;
    }
}
