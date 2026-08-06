// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SizeScanner.Avalonia.Views;

public sealed class BusySpinnerControl : Control
{
    private const double StrokeThickness = 4d;
    private const double IndicatorSweep = 110d;
    private const double DegreesPerTick = 18d;

    private readonly DispatcherTimer _timer;
    private bool _isAttached;
    private double _startAngle;

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<BusySpinnerControl, bool>(nameof(IsActive));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<BusySpinnerControl, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> IndicatorBrushProperty =
        AvaloniaProperty.Register<BusySpinnerControl, IBrush?>(nameof(IndicatorBrush));

    public BusySpinnerControl()
    {
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Render,
            (_, _) =>
            {
                _startAngle = (_startAngle + DegreesPerTick) % 360d;
                InvalidateVisual();
            });
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? IndicatorBrush
    {
        get => GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    static BusySpinnerControl()
    {
        AffectsRender<BusySpinnerControl>(
            IsActiveProperty,
            TrackBrushProperty,
            IndicatorBrushProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
            UpdateTimer();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!IsActive)
            return;

        var diameter = Math.Min(Bounds.Width, Bounds.Height);
        var radius = Math.Max(0, diameter / 2d - StrokeThickness / 2d);
        if (radius <= 0)
            return;

        var center = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        context.DrawEllipse(
            null,
            new Pen(TrackBrush ?? Brushes.Gray, StrokeThickness),
            center,
            radius,
            radius);

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(PointOnCircle(center, radius, _startAngle), isFilled: false);
            path.ArcTo(
                PointOnCircle(center, radius, _startAngle + IndicatorSweep),
                new Size(radius, radius),
                0,
                isLargeArc: false,
                SweepDirection.Clockwise);
            path.EndFigure(isClosed: false);
        }

        context.DrawGeometry(
            null,
            new Pen(IndicatorBrush ?? Brushes.White, StrokeThickness),
            geometry);
    }

    private void UpdateTimer()
    {
        if (_isAttached && IsActive)
            _timer.Start();
        else
            _timer.Stop();

        if (!IsActive)
            _startAngle = 0;
        InvalidateVisual();
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = (angle - 90d) * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
