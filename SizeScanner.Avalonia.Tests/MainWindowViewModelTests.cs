// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
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

    private sealed class RecordingDialogs : IDialogService
    {
        public System.Collections.Generic.List<(string Title, string Message)> InfoCalls { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowInfoAsync(string title, string message)
        {
            InfoCalls.Add((title, message));
            return Task.CompletedTask;
        }
    }

    private static MainWindowViewModel CreateVm(
        FsItem root,
        FakeSettings? settings = null,
        ChartViewModel? chart = null,
        FakeScanService? scan = null,
        RecordingDialogs? dialogs = null)
    {
        scan ??= new FakeScanService();
        dialogs ??= new RecordingDialogs();
        scan.RootResult ??= (_, _) => root;
        chart ??= new ChartViewModel(scan, new NoopFs(), dialogs);
        return new(scan, settings ?? new FakeSettings(), new FakeDrives(),
            new FakeElevation(), new FakeFolderPicker(), dialogs, chart);
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
        var chart = new ChartViewModel(new FakeScanService(), new NoopFs(), new RecordingDialogs());
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
        var dialogs = new RecordingDialogs();
        var pending = new TaskCompletionSource<FsItem>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scan.PendingRoot = pending;
        var vm = CreateVm(DriveRoot(), scan: scan, dialogs: dialogs);
        vm.Initialize();

        var scanTask = vm.ScanTargetAsync("D:\\data", isDrive: false);
        vm.CancelScanCommand.Execute(null);
        pending.SetCanceled(TestContext.Current.CancellationToken);
        await scanTask;

        Assert.False(vm.IsScanning);
        Assert.False(vm.IsBusy);
        Assert.False(vm.Chart.IsChartScanning);
        Assert.False(vm.DisplayProgressIsIndeterminate);
        Assert.Equal(0, vm.DisplayProgressValue);
        Assert.Empty(vm.StatusDetails);
        Assert.Equal("Scan cancelled", vm.StatusText);
        Assert.Empty(dialogs.InfoCalls);
    }

    [Fact]
    public async Task CancelScan_cancels_the_chart_scope_scan_when_no_root_scan_is_active()
    {
        var scan = new FakeScanService();
        var chart = new ChartViewModel(scan, new NoopFs(), new RecordingDialogs());
        var root = TestTree.Dir("C:\\", TestTree.Dir("Data", TestTree.File("f.bin", 10)));
        var vm = CreateVm(root, chart: chart, scan: scan);
        vm.Initialize();
        await vm.ScanTargetAsync("C:\\", isDrive: false);

        var dataDir = root.Items![0];
        var pendingScope = new TaskCompletionSource<FsItem>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scan.PendingScope = pendingScope;
        var scopeTask = chart.TryScopeAtAsync(dataDir);

        vm.CancelScanCommand.Execute(null);
        pendingScope.SetCanceled(TestContext.Current.CancellationToken);

        Assert.False(await scopeTask);
        Assert.False(chart.IsScopeScanning);
    }

    [Fact]
    public async Task RootRescanned_from_chart_replaces_scan_root_and_refreshes_inaccessible_pane()
    {
        var scan = new FakeScanService();
        var chart = new ChartViewModel(scan, new NoopFs(), new RecordingDialogs());
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

    [Fact]
    public async Task Scoped_scan_drives_main_window_progress_presentation()
    {
        var scan = new FakeScanService();
        var chart = new ChartViewModel(scan, new NoopFs(), new RecordingDialogs());
        var root = TestTree.Dir("C:\\", TestTree.Dir("Data", TestTree.File("f.bin", 10)));
        var vm = CreateVm(root, chart: chart, scan: scan);
        await vm.ScanTargetAsync("C:\\", isDrive: false);
        var pending = new TaskCompletionSource<FsItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        scan.PendingScope = pending;

        var scopeTask = chart.TryScopeAtAsync(root.Items![0]);

        Assert.True(vm.IsBusy);
        Assert.True(vm.DisplayProgressIsIndeterminate);
        Assert.Equal(0, vm.DisplayProgressValue);

        var progressChanged = PropertyChangedTestHelper.WaitForAsync(
            vm,
            nameof(MainWindowViewModel.DisplayProgressIsIndeterminate));
        scan.ScopeProgress!.Report(new ScanProgress("C:\\Data", 20, 42f, false));
        await progressChanged;

        Assert.False(vm.DisplayProgressIsIndeterminate);
        Assert.Equal(42, vm.DisplayProgressValue);
        Assert.Contains("C:\\Data", vm.DisplayStatusText);

        pending.SetResult(TestTree.Dir("Data", TestTree.File("f.bin", 10)));
        Assert.True(await scopeTask);
        Assert.False(vm.IsBusy);
        Assert.False(vm.DisplayProgressIsIndeterminate);
        Assert.Equal(0, vm.DisplayProgressValue);
    }

    [Fact]
    public async Task Root_scan_without_percentage_uses_indeterminate_progress()
    {
        var scan = new FakeScanService();
        var pending = new TaskCompletionSource<FsItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        scan.PendingRoot = pending;
        var vm = CreateVm(DriveRoot(), scan: scan);

        var scanTask = vm.ScanTargetAsync("D:\\data", isDrive: false);
        var statusChanged = PropertyChangedTestHelper.WaitForAsync(
            vm,
            nameof(MainWindowViewModel.StatusDetails));
        scan.RootProgress!.Report(new ScanProgress("D:\\data\\child", 100, null, false));
        await statusChanged;

        Assert.True(vm.DisplayProgressIsIndeterminate);
        Assert.Equal(0, vm.DisplayProgressValue);

        pending.SetResult(DriveRoot());
        await scanTask;
        Assert.False(vm.DisplayProgressIsIndeterminate);
    }

    [Fact]
    public async Task Failed_root_scan_clears_busy_progress_state()
    {
        var scan = new FakeScanService();
        var dialogs = new RecordingDialogs();
        var pending = new TaskCompletionSource<FsItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        scan.PendingRoot = pending;
        var vm = CreateVm(DriveRoot(), scan: scan, dialogs: dialogs);
        var scanTask = vm.ScanTargetAsync("D:\\data", isDrive: false);

        pending.SetException(new IOException("boom"));

        await scanTask;
        Assert.False(vm.IsBusy);
        Assert.False(vm.Chart.IsChartScanning);
        Assert.False(vm.DisplayProgressIsIndeterminate);
        Assert.Equal(0, vm.DisplayProgressValue);
        Assert.Empty(vm.StatusDetails);
        Assert.Equal("Scan failed", vm.StatusText);
        Assert.Equal([("Scan failed", "boom")], dialogs.InfoCalls);
    }

    [Fact]
    public async Task Toolbar_scan_commands_are_disabled_while_chart_is_scope_scanning()
    {
        var scan = new FakeScanService();
        var chart = new ChartViewModel(scan, new NoopFs(), new RecordingDialogs());
        var root = TestTree.Dir("C:\\", TestTree.Dir("Data", TestTree.File("f.bin", 10)));
        var vm = CreateVm(root, chart: chart, scan: scan);
        vm.Initialize();
        await vm.ScanTargetAsync("C:\\", isDrive: false);

        var dataDir = root.Items![0];
        var pendingScope = new TaskCompletionSource<FsItem>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scan.PendingScope = pendingScope;
        var scopeTask = chart.TryScopeAtAsync(dataDir); // sets Chart.IsScopeScanning

        Assert.True(chart.IsScopeScanning);
        Assert.False(vm.ScanDriveCommand.CanExecute(new DriveItem("C:", "C:\\")));
        Assert.False(vm.BrowseCommand.CanExecute(null));
        Assert.False(vm.RescanCommand.CanExecute(null));

        // A stale "Go to root"/"Go up" rescan must not let the toolbar race the shared
        // ScanService: ScanTargetAsync itself must refuse to call RunAsync while
        // Chart.IsScopeScanning is true, as a backstop behind the disabled CanExecute above.
        var rootCallsBefore = scan.RootCalls.Count;
        await vm.ScanTargetAsync("C:\\", isDrive: false);
        Assert.Equal(rootCallsBefore, scan.RootCalls.Count);

        pendingScope.SetCanceled(TestContext.Current.CancellationToken);
        Assert.False(await scopeTask);

        Assert.False(chart.IsScopeScanning);
        Assert.True(vm.ScanDriveCommand.CanExecute(new DriveItem("C:", "C:\\")));
        Assert.True(vm.RescanCommand.CanExecute(null));
    }

    [Fact]
    public async Task GoToRoot_is_refused_while_a_toolbar_root_scan_is_in_flight()
    {
        var scan = new FakeScanService();
        var chart = new ChartViewModel(scan, new NoopFs(), new RecordingDialogs());
        var root = TestTree.Dir("C:\\", TestTree.Dir("Data", TestTree.File("f.bin", 10)));
        var vm = CreateVm(root, chart: chart, scan: scan);
        vm.Initialize();
        await vm.ScanTargetAsync("C:\\", isDrive: false);

        var dataDir = root.Items![0];
        var scopedData = TestTree.Dir("Data", TestTree.File("f.bin", 10));
        scan.ScopeResult = _ => scopedData;
        await chart.TryScopeAtAsync(dataDir);

        // Mark the cached root stale, so GoToRootAsync must rescan it via _scan.RunAsync
        // rather than take the cheap "just drop back to the cached tree" path.
        chart.SetContextTarget(scopedData.Items![0]);
        await chart.DeleteCommand.ExecuteAsync(null);
        Assert.True(chart.IsScoped);

        var pendingRoot = new TaskCompletionSource<FsItem>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scan.PendingRoot = pendingRoot;
        var rescanTask = vm.ScanTargetAsync("C:\\", isDrive: false); // toolbar RunAsync in flight

        Assert.True(chart.IsRootScanInProgress);
        var rootCallsBefore = scan.RootCalls.Count;

        await chart.GoToRootCommand.ExecuteAsync(null);

        Assert.Equal(rootCallsBefore, scan.RootCalls.Count); // no second, racing RunAsync call
        Assert.True(chart.IsScoped); // refused: scope state left untouched

        pendingRoot.SetResult(root);
        await rescanTask;
        Assert.False(chart.IsRootScanInProgress);
    }
}
