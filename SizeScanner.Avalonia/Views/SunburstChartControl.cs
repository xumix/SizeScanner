// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SizeScanner.Avalonia.Charting;

namespace SizeScanner.Avalonia.Views;

public sealed class SunburstChartControl : Control
{
    public static readonly StyledProperty<SunburstChart?> ChartProperty =
        AvaloniaProperty.Register<SunburstChartControl, SunburstChart?>(nameof(Chart));

    private static readonly Pen SegmentBorder = new(Brushes.Black, 1);

    public SunburstChart? Chart
    {
        get => GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    static SunburstChartControl()
    {
        AffectsRender<SunburstChartControl>(ChartProperty);
    }

    public SunburstSegment? HitTestSegment(Point point) =>
        Chart is null ? null : SunburstHitTest.HitTest(Chart, point, Bounds.Size);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var chart = Chart;
        if (chart is null || chart.RingCount == 0)
            return;

        foreach (var segment in chart.Segments)
        {
            if (segment.IsPlaceholder)
                continue;

            var geometry = CreateSegmentGeometry(Bounds.Size, segment, chart.RingCount);
            context.DrawGeometry(new SolidColorBrush(segment.Color), SegmentBorder, geometry);
        }
    }

    private static StreamGeometry CreateSegmentGeometry(Size bounds, SunburstSegment segment, int ringCount)
    {
        var outerRadius = Math.Min(bounds.Width, bounds.Height) / 2d;
        var center = new Point(bounds.Width / 2d, bounds.Height / 2d);
        var innerHole = outerRadius * 0.18;
        var ringWidth = (outerRadius - innerHole) / ringCount;
        var segmentOuter = outerRadius - segment.RingIndex * ringWidth;
        var segmentInner = segmentOuter - ringWidth;
        var sweep = Math.Min(segment.SweepAngle, 359.999d);
        var largeArc = sweep > 180d;

        var outerStart = PointOnCircle(center, segmentOuter, segment.StartAngle);
        var outerEnd = PointOnCircle(center, segmentOuter, segment.StartAngle + sweep);
        var innerEnd = PointOnCircle(center, segmentInner, segment.StartAngle + sweep);
        var innerStart = PointOnCircle(center, segmentInner, segment.StartAngle);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(outerStart, isFilled: true);
            ctx.ArcTo(outerEnd, new Size(segmentOuter, segmentOuter), 0, largeArc, SweepDirection.Clockwise);
            ctx.LineTo(innerEnd);
            ctx.ArcTo(innerStart, new Size(segmentInner, segmentInner), 0, largeArc, SweepDirection.CounterClockwise);
            ctx.EndFigure(isClosed: true);
        }
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = (angle - 90d) * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
