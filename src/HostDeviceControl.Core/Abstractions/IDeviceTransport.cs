using System;
using System.Threading;
using System.Threading.Tasks;

namespace HostDeviceControl.Core.Abstractions;

public interface IDeviceTransport : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken);

    ValueTask WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken);
}
