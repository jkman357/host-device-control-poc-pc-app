// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Collections.Generic;
using System.Threading;

namespace HostDeviceControl.Core.Concurrency;

/// <summary>
/// Provides a thread-safe bounded FIFO buffer that discards the oldest item
/// when a producer exceeds the configured capacity.
/// </summary>
/// <typeparam name="T">Type of item retained by the buffer.</typeparam>
public sealed class BoundedDropOldestBuffer<T>
{
    private readonly object _sync = new();
    private readonly Queue<T> _items;
    private long _droppedItemCount;

    /// <summary>
    /// Initializes a new buffer with the specified maximum number of items.
    /// </summary>
    /// <param name="capacity">Maximum number of retained items.</param>
    public BoundedDropOldestBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Capacity must be greater than zero.");
        }

        Capacity = capacity;
        _items = new Queue<T>(capacity);
    }

    /// <summary>
    /// Gets the maximum number of items retained by this buffer.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Gets the current number of retained items.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// Gets the number of items discarded because the buffer was full.
    /// </summary>
    public long DroppedItemCount => Interlocked.Read(ref _droppedItemCount);

    /// <summary>
    /// Adds an item and discards the oldest retained item when capacity is full.
    /// </summary>
    /// <param name="item">Item to enqueue.</param>
    /// <returns><see langword="true"/> when an older item was discarded.</returns>
    public bool Enqueue(T item)
    {
        bool droppedOldest = false;

        lock (_sync)
        {
            if (_items.Count == Capacity)
            {
                _items.Dequeue();
                droppedOldest = true;
            }

            _items.Enqueue(item);
        }

        if (droppedOldest)
        {
            Interlocked.Increment(ref _droppedItemCount);
        }

        return droppedOldest;
    }

    /// <summary>
    /// Removes up to <paramref name="maximumItemCount"/> items and appends them
    /// to the supplied destination in FIFO order.
    /// </summary>
    /// <param name="destination">Destination collection.</param>
    /// <param name="maximumItemCount">Maximum number of items to remove.</param>
    /// <returns>The number of items removed.</returns>
    public int DrainTo(ICollection<T> destination, int maximumItemCount)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (maximumItemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumItemCount),
                maximumItemCount,
                "Maximum item count must be greater than zero.");
        }

        int drainedItemCount = 0;

        lock (_sync)
        {
            while ((_items.Count > 0) &&
                   (drainedItemCount < maximumItemCount))
            {
                destination.Add(_items.Dequeue());
                drainedItemCount++;
            }
        }

        return drainedItemCount;
    }

    /// <summary>
    /// Removes all retained items without resetting the lifetime drop counter.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _items.Clear();
        }
    }
}
