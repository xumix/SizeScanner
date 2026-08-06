// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Media;
using SizeScanner.Avalonia.Views;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

/// <summary>
/// Covers <see cref="BusySpinnerControl"/>'s public property contract without attaching it to a
/// visual tree. The current test host has no windowing platform (no Avalonia.Headless
/// dependency is permitted), so the timer's actual Start/Stop calls on
/// attach/detach cannot be observed here without reflection or a production-only test seam;
/// that behavior remains covered only by manual verification (see task-3-report.md).
/// Construction and property access run on <see cref="AvaloniaUiThread"/> because the control's
/// constructor starts a <c>DispatcherTimer</c>, which touches Avalonia's Dispatcher.
/// </summary>
public sealed class BusySpinnerControlTests
{
    [Fact]
    public void IsActive_defaults_to_false() =>
        AvaloniaUiThread.Invoke(() => Assert.False(new BusySpinnerControl().IsActive));

    [Fact]
    public void IsActive_can_be_toggled_repeatedly_while_detached_without_throwing() =>
        AvaloniaUiThread.Invoke(() =>
        {
            var spinner = new BusySpinnerControl();

            for (var i = 0; i < 20; i++)
                spinner.IsActive = !spinner.IsActive;

            // Even number of toggles from the false default returns to false; this
            // also exercises the detached branch of UpdateTimer (start guarded by
            // "attached to visual tree") without raising an exception.
            Assert.False(spinner.IsActive);
        });

    [Fact]
    public void TrackBrush_and_IndicatorBrush_round_trip() =>
        AvaloniaUiThread.Invoke(() =>
        {
            var spinner = new BusySpinnerControl
            {
                TrackBrush = Brushes.Red,
                IndicatorBrush = Brushes.Blue,
            };

            Assert.Same(Brushes.Red, spinner.TrackBrush);
            Assert.Same(Brushes.Blue, spinner.IndicatorBrush);
        });

    [Fact]
    public void Brushes_default_to_null_so_render_falls_back_to_defaults() =>
        AvaloniaUiThread.Invoke(() =>
        {
            var spinner = new BusySpinnerControl();

            Assert.Null(spinner.TrackBrush);
            Assert.Null(spinner.IndicatorBrush);
        });
}
