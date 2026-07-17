// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading.Tasks;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Models;
using SizeScanner.Avalonia.ViewModels;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class MainWindowViewModelTests
{
    private sealed class FakeSettings : ISettingsStore
    {
        public UserSettings Loaded { get; set; } = new();
        public UserSettings Saved { get; private set; } = new();
        public UserSettings Load() => Loaded;
        public void Save(UserSettings settings) => Saved = settings;
    }

    private sealed class FakeDrives : IDriveProvider
    {
        public System.Collections.Generic.IReadOnlyList<DriveItem> GetReadyDrives() =>
            new[] { new DriveItem("C:", "C:\\") };
    }

    private sealed class FakeElevation : IElevationService
    {
        public bool IsRunningAsAdministrator() => true;
        public bool TryRelaunchAsAdministrator(out string? error) { error = null; return true; }
    }

    private sealed class FakeFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

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

    private static MainWindowViewModel CreateVm(
        FsItem root,
        FakeSettings? settings = null,
        ChartViewModel? chart = null,
        FakeScanService? scan = null)
    {
        scan ??= new FakeScanService();
        scan.RootResult ??= (_, _) => root;
        chart ??= new ChartViewModel(scan, new NoopFs(), new NoopDialogs());
        return new(scan, settings ?? new FakeSettings(), new FakeDrives(),
            new FakeElevation(), new FakeFolderPicker(), chart);
    }

    private static FsItem DriveRoot() =>
        TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 0),
            TestTree.File("page.sys", 200));

    [Fact]
    public void Initialize_populates_drive_buttons_and_default_options()
    {
        var vm = CreateVm(DriveRoot());
        vm.Initialize();

        Assert.Single(vm.Drives);
        Assert.Equal(4, vm.FilterIndex);
        Assert.Equal(1, vm.FreeSpaceIndex);
        Assert.Equal(MainWindowViewModel.DefaultInaccessiblePaneWidth, vm.InaccessiblePaneWidth);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public void Initialize_restores_splitter_distance_from_settings()
    {
        var settings = new FakeSettings { Loaded = new UserSettings { SplitterDistance = 520 } };
        var vm = CreateVm(DriveRoot(), settings);
        vm.Initialize();

        Assert.Equal(520, vm.InaccessiblePaneWidth);
    }

    [Fact]
    public void SaveOnClose_persists_splitter_distance()
    {
        var settings = new FakeSettings();
        var vm = CreateVm(DriveRoot(), settings);
        vm.SaveOnClose(1000, 700, 480);

        Assert.Equal(480, settings.Saved.SplitterDistance);
    }

    [Fact]
    public async Task ScanDirectory_sets_chart_and_ready_state()
    {
        var vm = CreateVm(DriveRoot());
        vm.Initialize();

        await vm.ScanTargetAsync("D:\\data", isDrive: false);

        Assert.False(vm.IsScanning);
        Assert.True(vm.CanRescan);
        Assert.NotEmpty(vm.Chart.Layout.Segments);
        Assert.Equal("Ready", vm.StatusText);
    }

    [Fact]
    public async Task ChangingOptions_after_scan_saves_settings()
    {
        var settings = new FakeSettings();
        var vm = CreateVm(DriveRoot(), settings);
        vm.Initialize();
        await vm.ScanTargetAsync("D:\\data", isDrive: false);

        vm.FilterIndex = 2;

        Assert.Equal(2, settings.Saved.FilterIndex);
    }

    [Fact]
    public void DisplayStatusText_uses_chart_delete_status_while_deleting()
    {
        var chart = new ChartViewModel(new FakeScanService(), new NoopFs(), new NoopDialogs());
        var vm = CreateVm(DriveRoot(), chart: chart);

        chart.DeleteStatusText = "Moving to Recycle Bin: C:\\page.sys";
        chart.IsDeleting = true;

        Assert.Equal("Moving to Recycle Bin: C:\\page.sys", vm.DisplayStatusText);

        chart.IsDeleting = false;

        Assert.Equal("Ready", vm.DisplayStatusText);
    }

    [Fact]
    public async Task CancelScan_cancels_an_active_root_scan()
    {
        var scan = new FakeScanService();
        var pending = new TaskCompletionSource<FsItem>();
        scan.PendingRoot = pending;
        var vm = CreateVm(DriveRoot(), scan: scan);
        vm.Initialize();

        var scanTask = vm.ScanTargetAsync("D:\\data", isDrive: false);
        vm.CancelScanCommand.Execute(null);
        pending.SetCanceled();
        await scanTask;

        Assert.False(vm.IsScanning);
        Assert.Equal("Scan cancelled", vm.StatusText);
    }

    [Fact]
    public async Task CancelScan_cancels_the_chart_scope_scan_when_no_root_scan_is_active()
    {
        var scan = new FakeScanService();
        var chart = new ChartViewModel(scan, new NoopFs(), new NoopDialogs());
        var root = TestTree.Dir("C:\\", TestTree.Dir("Data", TestTree.File("f.bin", 10)));
        var vm = CreateVm(root, chart: chart, scan: scan);
        vm.Initialize();
        await vm.ScanTargetAsync("C:\\", isDrive: false);

        var dataDir = root.Items![0];
        var pendingScope = new TaskCompletionSource<FsItem>();
        scan.PendingScope = pendingScope;
        var scopeTask = chart.TryScopeAtAsync(dataDir);

        vm.CancelScanCommand.Execute(null);
        pendingScope.SetCanceled();

        Assert.False(await scopeTask);
        Assert.False(chart.IsScopeScanning);
    }

    [Fact]
    public async Task RootRescanned_from_chart_replaces_scan_root_and_refreshes_inaccessible_pane()
    {
        var scan = new FakeScanService();
        var chart = new ChartViewModel(scan, new NoopFs(), new NoopDialogs());
        var root = TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 50),
            TestTree.Dir("Users", TestTree.File("profile.dat", 300)));
        var vm = CreateVm(root, chart: chart, scan: scan);
        vm.Initialize();
        await vm.ScanTargetAsync("C:\\", isDrive: true);

        Assert.Equal(Humanize.Size(50), vm.InaccessibleTotalSize);

        var users = root.Items![2];
        var scopedUsers = TestTree.Dir("Users", TestTree.File("profile.dat", 300));
        scan.ScopeResult = _ => scopedUsers;
        await chart.TryScopeAtAsync(users);

        chart.SetContextTarget(scopedUsers.Items![0]);
        await chart.DeleteCommand.ExecuteAsync(null); // marks the cached root stale

        var rescannedRoot = TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 999));
        scan.RootResult = (_, _) => rescannedRoot;

        await chart.GoToRootCommand.ExecuteAsync(null);

        Assert.False(chart.IsScoped);
        Assert.Equal(Humanize.Size(999), vm.InaccessibleTotalSize);
    }
}
