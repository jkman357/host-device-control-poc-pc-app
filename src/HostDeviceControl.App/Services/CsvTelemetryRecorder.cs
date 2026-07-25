using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HostDeviceControl.Core.Models;

namespace HostDeviceControl.App.Services;

public sealed class CsvTelemetryRecorder : IAsyncDisposable
{
    private const int QueueCapacity = 4096;

    private Channel<TelemetrySample>? _channel;
    private Task? _writerTask;
    private long _droppedSampleCount;

    public bool IsRecording => _writerTask is not null;

    public long DroppedSampleCount => Interlocked.Read(ref _droppedSampleCount);

    public async Task StartAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("Recording is already active.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var channel = Channel.CreateBounded<TelemetrySample>(
            new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            65536);

        try
        {
            await writer.WriteLineAsync(
                "sample_counter,device_tick_us,host_received_utc,sine_value,status_flags")
                .ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await writer.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        Interlocked.Exchange(ref _droppedSampleCount, 0);
        _channel = channel;
        _writerTask = Task.Run(
            () => WriterLoopAsync(writer, channel.Reader),
            CancellationToken.None);
    }

    public bool TryRecord(TelemetrySample sample)
    {
        Channel<TelemetrySample>? channel = _channel;
        if (channel is null)
        {
            return false;
        }

        bool written = channel.Writer.TryWrite(sample);
        if (!written)
        {
            Interlocked.Increment(ref _droppedSampleCount);
        }

        return written;
    }

    public async Task StopAsync()
    {
        Task? writerTask = _writerTask;
        Channel<TelemetrySample>? channel = _channel;

        _writerTask = null;
        _channel = null;

        if (writerTask is null || channel is null)
        {
            return;
        }

        channel.Writer.TryComplete();
        await writerTask.ConfigureAwait(false);
    }

    private static async Task WriterLoopAsync(
        StreamWriter writer,
        ChannelReader<TelemetrySample> reader)
    {
        await using StreamWriter ownedWriter = writer;
        int unflushedRows = 0;

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
            await ownedWriter.WriteLineAsync(line).ConfigureAwait(false);

            unflushedRows++;
            if (unflushedRows >= 200)
            {
                await ownedWriter.FlushAsync().ConfigureAwait(false);
                unflushedRows = 0;
            }
        }

        await ownedWriter.FlushAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
