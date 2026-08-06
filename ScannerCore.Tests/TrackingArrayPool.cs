// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;
using System.Threading;

namespace ScannerCore.Tests;

/// <summary>
/// Wraps <see cref="ArrayPool{T}.Shared"/> to count outstanding rentals, so tests can
/// assert the walker never holds more buffers than its configured degree of
/// parallelism. Production code always defaults to <see cref="ArrayPool{T}.Shared"/>
/// directly via <see cref="BoundedDirectoryWalker.BufferPool"/>; this type exists only
/// so tests can observe rental counts without changing that default or the walker's
/// public constructor.
/// </summary>
internal sealed class TrackingArrayPool : ArrayPool<byte>
{
    private int _current;
    private int _peak;

    public int Peak => Volatile.Read(ref _peak);

    public override byte[] Rent(int minimumLength)
    {
        var now = Interlocked.Increment(ref _current);
        ConcurrencyProbe.RecordPeak(ref _peak, now);
        return Shared.Rent(minimumLength);
    }

    public override void Return(byte[] array, bool clearArray = false)
    {
        Interlocked.Decrement(ref _current);
        Shared.Return(array, clearArray);
    }
}
