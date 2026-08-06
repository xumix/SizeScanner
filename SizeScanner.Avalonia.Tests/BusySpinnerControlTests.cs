// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using SizeScanner.Avalonia.Views;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

/// <summary>
/// Covers <see cref="BusySpinnerControl"/>'s public property contract and dispatcher-timer
/// lifecycle without requiring a windowing platform. Construction, lifecycle callbacks, and
/// property access run on <see cref="AvaloniaUiThread"/> because the control owns a
/// <c>DispatcherTimer</c>, which touches Avalonia's Dispatcher.
/// </summary>
public sealed class BusySpinnerControlTests
{
    [Fact]
    public void Inactive_detached_spinner_does_not_start_its_timer() =>
        AvaloniaUiThread.Invoke(() =>
        {
            var spinner = new BusySpinnerControl();

            Assert.False(GetTimer(spinner).IsEnabled);
        });

    [Fact]
    public void Timer_runs_only_while_spinner_is_attached_and_active() =>
        AvaloniaUiThread.Invoke(() =>
        {
            var spinner = new BusySpinnerControl { IsActive = true };
            var timer = GetTimer(spinner);
            var lifecycleArgs = (VisualTreeAttachmentEventArgs)
                RuntimeHelpers.GetUninitializedObject(typeof(VisualTreeAttachmentEventArgs));

            Assert.False(timer.IsEnabled);

            InvokeLifecycle(spinner, "OnAttachedToVisualTree", lifecycleArgs);
            try
            {
                Assert.True(timer.IsEnabled);

                spinner.IsActive = false;
                Assert.False(timer.IsEnabled);

                spinner.IsActive = true;
                Assert.True(timer.IsEnabled);
            }
            finally
            {
                InvokeLifecycle(spinner, "OnDetachedFromVisualTree", lifecycleArgs);
            }

            Assert.False(timer.IsEnabled);
        });

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

    private static DispatcherTimer GetTimer(BusySpinnerControl spinner) =>
        Assert.IsType<DispatcherTimer>(
            typeof(BusySpinnerControl)
                .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(spinner));

    private static void InvokeLifecycle(
        BusySpinnerControl spinner,
        string methodName,
        VisualTreeAttachmentEventArgs args) =>
        typeof(BusySpinnerControl)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(spinner, [args]);
}
