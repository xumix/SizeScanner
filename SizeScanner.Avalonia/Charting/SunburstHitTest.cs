// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia;

namespace SizeScanner.Avalonia.Charting;

public static class SunburstHitTest
{
    public static SunburstSegment? HitTest(
        SunburstChart chart,
        Point point,
        Size bounds)
    {
        if (chart.RingCount <= 0 || chart.Segments.Count == 0)
            return null;

        var center = new Point(bounds.Width / 2d, bounds.Height / 2d);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var ringIndex = SunburstRingLayout.ResolveRingIndex(distance, bounds, chart.RingCount);
        if (ringIndex is null)
            return null;

        var angle = (Math.Atan2(dy, dx) * 180d / Math.PI + 90d + 360d) % 360d;
        var ring = chart.ActionableSegmentsByRing(ringIndex.Value);
        if (ring.Count == 0)
            return null;

        if (ring.Count == 1 && ring[0].SweepAngle >= 360d)
            return ring[0];

        // Rightmost segment whose normalized start angle <= angle.
        var lo = 0;
        var hi = ring.Count - 1;
        var idx = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Normalize(ring[mid].StartAngle) <= angle)
            {
                idx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (idx >= 0 && Contains(ring[idx], angle))
            return ring[idx];

        // The last segment may wrap past 360 and cover small angles before the first start.
        var last = ring[ring.Count - 1];
        if (Contains(last, angle))
            return last;

        return null;
    }

    private static bool Contains(SunburstSegment segment, double angle)
    {
        if (segment.SweepAngle >= 360d)
            return true;
        var delta = (angle - Normalize(segment.StartAngle) + 360d) % 360d;
        return delta < segment.SweepAngle;
    }

    private static double Normalize(double angle) => (angle % 360d + 360d) % 360d;
}
