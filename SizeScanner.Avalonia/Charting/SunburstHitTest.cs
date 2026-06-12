// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
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
        return chart.Segments.FirstOrDefault(s =>
            s.IsActionable &&
            s.RingIndex == ringIndex.Value &&
            ContainsAngle(s, angle));
    }

    private static bool ContainsAngle(SunburstSegment segment, double angle)
    {
        var start = Normalize(segment.StartAngle);
        var end = Normalize(segment.EndAngle);
        return segment.SweepAngle >= 360d ||
               (start <= end
                   ? angle >= start && angle < end
                   : angle >= start || angle < end);
    }

    private static double Normalize(double angle) => (angle % 360d + 360d) % 360d;
}
