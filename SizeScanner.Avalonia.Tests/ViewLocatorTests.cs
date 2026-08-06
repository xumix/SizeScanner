// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;
using Avalonia.Controls;
using SizeScanner.Avalonia;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.ViewModels;
using SizeScanner.Avalonia.Views;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class ViewLocatorTests
{
    private readonly ViewLocator _locator = new();

    [Fact]
    public void Match_returns_true_for_view_models() =>
        Assert.True(_locator.Match(CreateChartViewModel()));

    [Fact]
    public void Match_returns_false_for_non_view_models() =>
        Assert.False(_locator.Match("not a view model"));

    [Fact]
    public void Build_returns_null_for_null() =>
        Assert.Null(_locator.Build(null));

    [Fact]
    public void Build_returns_ChartView_for_ChartViewModel()
    {
        var view = AvaloniaUiThread.Invoke(() => _locator.Build(CreateChartViewModel()));

        Assert.IsType<ChartView>(view);
    }

    private static ChartViewModel CreateChartViewModel() =>
        new(new FakeScanService(), new NoopFs(), new NoopDialogs());

    [Fact]
    public void Build_returns_TextBlock_for_unregistered_view_model() =>
        AvaloniaUiThread.Invoke(() =>
        {
            var view = _locator.Build(new UnregisteredViewModel());

            var textBlock = Assert.IsType<TextBlock>(view);
            Assert.Contains(nameof(UnregisteredViewModel), textBlock.Text);
        });

    [Fact]
    public void ChartView_contains_app_owned_busy_spinner()
    {
        var spinner = AvaloniaUiThread.Invoke(() =>
            new ChartView().FindControl<BusySpinnerControl>("PART_ScanSpinner"));

        Assert.NotNull(spinner);
    }

    [Fact]
    public void ChartView_scan_overlay_is_configured_to_block_pointer_input() =>
        AvaloniaUiThread.Invoke(() =>
        {
            var view = new ChartView();

            var overlay = view.FindControl<Grid>("PART_ScanOverlay");

            Assert.NotNull(overlay);
            // Avalonia panels only participate in hit-testing when Background is
            // non-null; a null background lets pointer input fall through to the
            // chart underneath even while the overlay is visible.
            Assert.NotNull(overlay!.Background);
            Assert.True(overlay.IsHitTestVisible);
        });

    [Fact]
    public void ChartView_overlay_and_spinner_track_IsChartScanning() =>
        AvaloniaUiThread.Invoke(() =>
        {
            var vm = CreateChartViewModel();
            var view = new ChartView { DataContext = vm };
            var overlay = view.FindControl<Grid>("PART_ScanOverlay")!;
            var spinner = view.FindControl<BusySpinnerControl>("PART_ScanSpinner")!;
            var chart = view.FindControl<SunburstChartControl>("PART_Chart")!;

            Assert.False(overlay.IsVisible);
            Assert.False(spinner.IsActive);
            Assert.True(chart.IsEnabled);

            vm.IsRootScanInProgress = true;

            Assert.True(overlay.IsVisible);
            Assert.True(spinner.IsActive);
            // The chart is disabled (not just visually covered) while scanning so that
            // keyboard focus and its ContextMenu commands (Delete, Open in Explorer)
            // cannot race a concurrent scan; Avalonia routes neither pointer nor
            // keyboard/context input to disabled elements.
            Assert.False(chart.IsEnabled);

            vm.IsRootScanInProgress = false;

            Assert.False(overlay.IsVisible);
            Assert.False(spinner.IsActive);
            Assert.True(chart.IsEnabled);
        });

    private sealed class UnregisteredViewModel : ViewModelBase;

    private sealed class NoopFs : IFileSystemActions
    {
        public void ShowInExplorer(string path) { }
        public Task<DeleteResult> DeleteAsync(string path, bool permanent) =>
            Task.FromResult(new DeleteResult(true, null));
    }

    private sealed class NoopDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }
}
