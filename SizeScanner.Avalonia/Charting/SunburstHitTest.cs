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
        Size bounds,
        double innerHoleRatio = 0.18)
    {
        if (chart.RingCount <= 0 || chart.Segments.Count == 0)
            return null;

        var outerRadius = Math.Min(bounds.Width, bounds.Height) / 2d;
        if (outerRadius <= 0)
            return null;

        var center = new Point(bounds.Width / 2d, bounds.Height / 2d);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var innerRadius = outerRadius * innerHoleRatio;
        if (distance < innerRadius || distance > outerRadius)
            return null;

        var ringWidth = (outerRadius - innerRadius) / chart.RingCount;
        var ringIndex = (int)Math.Floor((outerRadius - distance) / ringWidth);
        if (ringIndex == chart.RingCount)
            ringIndex--;

        var angle = (Math.Atan2(dy, dx) * 180d / Math.PI + 90d + 360d) % 360d;
        return chart.Segments.FirstOrDefault(s =>
            s.IsActionable &&
            s.RingIndex == ringIndex &&
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
