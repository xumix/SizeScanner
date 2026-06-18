// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ScannerCore;
using SizeScanner.Avalonia.Charting;

namespace SizeScanner.Avalonia.Views;

public sealed class SunburstChartControl : Control
{
    private readonly record struct CachedSegment(IImmutableBrush Brush, StreamGeometry Geometry);

    // Cache key for the center label: everything the rendered FormattedText depends on.
    // Font sizes derive from Bounds + RingCount, so those two are enough to detect changes.
    private readonly record struct CenterLabelKey(long TotalSize, Size Bounds, int RingCount, IBrush LabelBrush);

    // Geometry + fill brush per segment, rebuilt only when the chart or bounds size change.
    // HoveredSegment changes on every pointer move, so keeping this out of the per-render path
    // avoids re-allocating one StreamGeometry + brush per segment on each mouse move.
    private SunburstChart? _cacheChart;
    private Size _cacheSize;
    private CachedSegment[]? _segmentCache;
    private Dictionary<SunburstSegment, StreamGeometry>? _geometryBySegment;

    private Pen? _segmentBorderPen;
    private IBrush? _segmentBorderPenBrush;
    private Pen? _hoverOutlinePen;
    private IBrush? _hoverOutlinePenBrush;

    private CenterLabelKey _labelCacheKey;
    private FormattedText? _centerTitleText;
    private FormattedText? _centerSizeText;

    public static readonly StyledProperty<SunburstChart?> ChartProperty =
        AvaloniaProperty.Register<SunburstChartControl, SunburstChart?>(nameof(Chart));

    public static readonly StyledProperty<SunburstSegment?> HoveredSegmentProperty =
        AvaloniaProperty.Register<SunburstChartControl, SunburstSegment?>(nameof(HoveredSegment));

    public static readonly StyledProperty<IBrush?> SegmentBorderBrushProperty =
        AvaloniaProperty.Register<SunburstChartControl, IBrush?>(nameof(SegmentBorderBrush));

    public static readonly StyledProperty<IBrush?> HoverOutlineBrushProperty =
        AvaloniaProperty.Register<SunburstChartControl, IBrush?>(nameof(HoverOutlineBrush));

    public static readonly StyledProperty<IBrush?> CenterLabelBrushProperty =
        AvaloniaProperty.Register<SunburstChartControl, IBrush?>(nameof(CenterLabelBrush));

    public SunburstChart? Chart
    {
        get => GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    public SunburstSegment? HoveredSegment
    {
        get => GetValue(HoveredSegmentProperty);
        set => SetValue(HoveredSegmentProperty, value);
    }

    public IBrush? SegmentBorderBrush
    {
        get => GetValue(SegmentBorderBrushProperty);
        set => SetValue(SegmentBorderBrushProperty, value);
    }

    public IBrush? HoverOutlineBrush
    {
        get => GetValue(HoverOutlineBrushProperty);
        set => SetValue(HoverOutlineBrushProperty, value);
    }

    public IBrush? CenterLabelBrush
    {
        get => GetValue(CenterLabelBrushProperty);
        set => SetValue(CenterLabelBrushProperty, value);
    }

    static SunburstChartControl()
    {
        AffectsRender<SunburstChartControl>(
            ChartProperty,
            HoveredSegmentProperty,
            SegmentBorderBrushProperty,
            HoverOutlineBrushProperty,
            CenterLabelBrushProperty);
    }

    public SunburstSegment? HitTestSegment(Point point) =>
        Chart is null ? null : SunburstHitTest.HitTest(Chart, point, Bounds.Size);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var chart = Chart;
        if (chart is null || chart.RingCount == 0)
            return;

        var size = Bounds.Size;
        var cache = EnsureSegmentCache(chart, size);

        var segmentBorder = GetPen(ref _segmentBorderPen, ref _segmentBorderPenBrush, SegmentBorderBrush, 1);
        foreach (var cached in cache)
            context.DrawGeometry(cached.Brush, segmentBorder, cached.Geometry);

        var hovered = HoveredSegment;
        if (hovered is not null)
        {
            // The hovered segment is one of chart.Segments (resolved via hit testing), so its
            // outline reuses the cached fill geometry. Fall back to a fresh build only if the
            // reference is stale (e.g. cache rebuilt after the hover was set).
            if (_geometryBySegment is null || !_geometryBySegment.TryGetValue(hovered, out var hoveredGeometry))
                hoveredGeometry = CreateSegmentGeometry(size, hovered, chart.RingCount);

            var hoverOutline = GetPen(ref _hoverOutlinePen, ref _hoverOutlinePenBrush, HoverOutlineBrush, 3);
            context.DrawGeometry(null, hoverOutline, hoveredGeometry);
        }

        DrawCenterLabel(context, chart, size);
    }

    private void DrawCenterLabel(DrawingContext context, SunburstChart chart, Size bounds)
    {
        var labelBrush = CenterLabelBrush ?? Brushes.Black;
        EnsureCenterLabel(new CenterLabelKey(chart.UsedTotalSize, bounds, chart.RingCount, labelBrush));

        if (_centerTitleText is null || _centerSizeText is null)
            return;

        var totalHeight = _centerTitleText.Height + _centerSizeText.Height;
        var originY = bounds.Height / 2d - totalHeight / 2d;
        var centerX = bounds.Width / 2d;

        context.DrawText(_centerTitleText, new Point(centerX - _centerTitleText.Width / 2d, originY));
        context.DrawText(
            _centerSizeText,
            new Point(centerX - _centerSizeText.Width / 2d, originY + _centerTitleText.Height));
    }

    private void EnsureCenterLabel(CenterLabelKey key)
    {
        if (_centerTitleText is not null && _centerSizeText is not null && _labelCacheKey == key)
            return;

        var (innerHole, _, _) = SunburstRingLayout.GetChartRadii(key.Bounds, key.RingCount);
        var titleFontSize = Math.Clamp(innerHole * 0.42, 10d, 18d);
        var sizeFontSize = Math.Clamp(innerHole * 0.36, 9d, 15d);

        var typeface = Typeface.Default;
        _centerTitleText = new FormattedText(
            "Total",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            titleFontSize,
            key.LabelBrush);
        _centerSizeText = new FormattedText(
            Humanize.Size(key.TotalSize),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            sizeFontSize,
            key.LabelBrush);

        _labelCacheKey = key;
    }

    private CachedSegment[] EnsureSegmentCache(SunburstChart chart, Size size)
    {
        if (_segmentCache is not null && ReferenceEquals(_cacheChart, chart) && _cacheSize == size)
            return _segmentCache;

        var segments = chart.Segments;
        var cache = new CachedSegment[segments.Count];
        var bySegment = new Dictionary<SunburstSegment, StreamGeometry>(
            segments.Count,
            ReferenceEqualityComparer.Instance);

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var geometry = CreateSegmentGeometry(size, segment, chart.RingCount);
            cache[i] = new CachedSegment(new ImmutableSolidColorBrush(segment.Color), geometry);
            bySegment[segment] = geometry;
        }

        _segmentCache = cache;
        _geometryBySegment = bySegment;
        _cacheChart = chart;
        _cacheSize = size;
        return cache;
    }

    private static Pen GetPen(ref Pen? pen, ref IBrush? penBrush, IBrush? brush, double thickness)
    {
        var effective = brush ?? Brushes.Black;
        if (pen is null || !ReferenceEquals(penBrush, effective))
        {
            pen = new Pen(effective, thickness);
            penBrush = effective;
        }
        return pen;
    }

    private static StreamGeometry CreateSegmentGeometry(Size bounds, SunburstSegment segment, int ringCount)
    {
        var center = new Point(bounds.Width / 2d, bounds.Height / 2d);
        var (segmentInner, segmentOuter) = SunburstRingLayout.GetSegmentRadii(segment.RingIndex, bounds, ringCount);
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
