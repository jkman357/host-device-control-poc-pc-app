// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HostDeviceControl.Core.Models;

namespace HostDeviceControl.App.Services;

/// <summary>
/// Records telemetry to CSV through a bounded single-reader channel. Queue loss
/// is counted and reported; it is never represented as complete recording.
/// </summary>
public sealed class CsvTelemetryRecorder : IAsyncDisposable
{
    private const int QueueCapacity = 4096;
    private const int FileBufferSizeBytes = 65536;
    private const int RowsPerFlush = 200;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private Channel<TelemetrySample>? _channel;
    private Task? _writerTask;
    private long _droppedSampleCount;
    private int _overrunSignaled;
    private bool _disposed;

    /// <summary>
    /// Raised once per recording session when the bounded queue first rejects a
    /// sample. The argument is the current lifetime drop count for that session.
    /// </summary>
    public event Action<long>? OverrunDetected;

    public bool IsRecording => Volatile.Read(ref _writerTask) is not null;

    public long DroppedSampleCount =>
        Interlocked.Read(ref _droppedSampleCount);

    /// <summary>
    /// Starts a new CSV file and waits until the header is successfully written.
    /// File creation is isolated from the UI thread by the owned writer task.
    /// </summary>
    public async Task StartAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_writerTask is not null)
            {
                throw new InvalidOperationException(
                    "Recording is already active.");
            }

            var channel = Channel.CreateBounded<TelemetrySample>(
                new BoundedChannelOptions(QueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false
                });
            var ready = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Interlocked.Exchange(ref _droppedSampleCount, 0);
            Interlocked.Exchange(ref _overrunSignaled, 0);

            Task writerTask = Task.Run(
                () => WriterLoopAsync(filePath, channel.Reader, ready),
                CancellationToken.None);
            _channel = channel;
            _writerTask = writerTask;

            try
            {
                await ready.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                channel.Writer.TryComplete();

                try
                {
                    await writerTask.ConfigureAwait(false);
                }
                catch
                {
                    // The original startup exception is preserved below.
                }

                _channel = null;
                _writerTask = null;
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Attempts to enqueue one sample without blocking the receive loop.
    /// </summary>
    public bool TryRecord(TelemetrySample sample)
    {
        Channel<TelemetrySample>? channel = Volatile.Read(ref _channel);
        if (channel is null)
        {
            return false;
        }

        bool isWritten = channel.Writer.TryWrite(sample);
        if (isWritten)
        {
            return true;
        }

        long droppedSampleCount =
            Interlocked.Increment(ref _droppedSampleCount);
        if (Interlocked.Exchange(ref _overrunSignaled, 1) == 0)
        {
            PublishOverrun(droppedSampleCount);
        }

        return false;
    }

    /// <summary>
    /// Completes the queue and waits for the writer within the caller-owned
    /// cancellation bound. If cancellation occurs, ownership is retained so a
    /// later stop or disposal can finish the same writer task.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Task? writerTask = _writerTask;
            Channel<TelemetrySample>? channel = _channel;

            if ((writerTask is null) || (channel is null))
            {
                return;
            }

            channel.Writer.TryComplete();

            try
            {
                await writerTask.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (writerTask.IsCompleted)
                {
                    _writerTask = null;
                    _channel = null;
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static async Task WriterLoopAsync(
        string filePath,
        ChannelReader<TelemetrySample> reader,
        TaskCompletionSource<bool> ready)
    {
        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                FileBufferSizeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                FileBufferSizeBytes);

            await writer.WriteLineAsync(
                "sample_counter,device_tick_us,host_received_utc," +
                "sine_value,status_flags").ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            ready.TrySetResult(true);

            int unflushedRowCount = 0;

            await foreach (TelemetrySample sample in
                           reader.ReadAllAsync().ConfigureAwait(false))
            {
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2:O},{3:R},0x{4:X4}",
                    sample.SampleCounter,
                    sample.DeviceTickUs,
                    sample.HostReceivedUtc,
                    sample.SineValue,
                    sample.StatusFlags);
                await writer.WriteLineAsync(line).ConfigureAwait(false);

                unflushedRowCount++;
                if (unflushedRowCount >= RowsPerFlush)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                    unflushedRowCount = 0;
                }
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ready.TrySetException(exception);
            throw;
        }
    }

    private void PublishOverrun(long droppedSampleCount)
    {
        Action<long>? handlers = OverrunDetected;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<long>)subscriber)(droppedSampleCount);
            }
            catch
            {
                // Recorder queue semantics must not depend on UI subscribers.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifecycleGate.Dispose();
        _disposed = true;
    }
}
