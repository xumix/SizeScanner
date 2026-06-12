// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia;

namespace SizeScanner.Avalonia.Charting;

/// <summary>
/// Shared polar layout for sunburst rings. Level 0 (RingIndex 0) is the innermost band
/// just outside the center hole; deeper levels move outward toward the chart edge.
/// </summary>
internal static class SunburstRingLayout
{
    public const double InnerHoleRatio = 0.18;

    public static (double InnerRadius, double OuterRadius, double RingWidth) GetChartRadii(Size bounds, int ringCount)
    {
        var chartOuter = Math.Min(bounds.Width, bounds.Height) / 2d;
        var innerHole = chartOuter * InnerHoleRatio;
        var ringWidth = ringCount > 0 ? (chartOuter - innerHole) / ringCount : 0d;
        return (innerHole, chartOuter, ringWidth);
    }

    public static (double Inner, double Outer) GetSegmentRadii(int ringIndex, Size bounds, int ringCount)
    {
        var (innerHole, _, ringWidth) = GetChartRadii(bounds, ringCount);
        var segmentInner = innerHole + ringIndex * ringWidth;
        return (segmentInner, segmentInner + ringWidth);
    }

    public static int? ResolveRingIndex(double distance, Size bounds, int ringCount)
    {
        if (ringCount <= 0)
            return null;

        var (innerHole, chartOuter, ringWidth) = GetChartRadii(bounds, ringCount);
        if (distance < innerHole || distance > chartOuter || ringWidth <= 0)
            return null;

        var ringIndex = (int)Math.Floor((distance - innerHole) / ringWidth);
        if (ringIndex >= ringCount)
            ringIndex = ringCount - 1;
        return ringIndex;
    }
}
