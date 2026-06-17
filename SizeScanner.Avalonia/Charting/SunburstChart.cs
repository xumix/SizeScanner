// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace SizeScanner.Avalonia.Charting;

public sealed record SunburstChart(
    IReadOnlyList<SunburstSegment> Segments,
    int RingCount,
    long TotalSize)
{
    private IReadOnlyList<SunburstSegment>[]? _ringIndex;

    /// <summary>
    /// Actionable segments on the given ring, sorted ascending by <see cref="SunburstSegment.StartAngle"/>.
    /// Built lazily on first use and reused for subsequent hit tests.
    /// </summary>
    public IReadOnlyList<SunburstSegment> ActionableSegmentsByRing(int ringIndex)
    {
        _ringIndex ??= BuildRingIndex();
        return ringIndex >= 0 && ringIndex < _ringIndex.Length
            ? _ringIndex[ringIndex]
            : Array.Empty<SunburstSegment>();
    }

    private IReadOnlyList<SunburstSegment>[] BuildRingIndex()
    {
        var ringCount = Math.Max(0, RingCount);
        var buckets = new List<SunburstSegment>[ringCount];
        for (var r = 0; r < ringCount; r++)
            buckets[r] = new List<SunburstSegment>();

        foreach (var segment in Segments)
            if (segment.IsActionable && segment.RingIndex >= 0 && segment.RingIndex < ringCount)
                buckets[segment.RingIndex].Add(segment);

        var result = new IReadOnlyList<SunburstSegment>[ringCount];
        for (var r = 0; r < ringCount; r++)
        {
            var array = buckets[r].ToArray();
            Array.Sort(array, static (a, b) => a.StartAngle.CompareTo(b.StartAngle));
            result[r] = array;
        }
        return result;
    }
}
