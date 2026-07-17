// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading.Tasks;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Charting;
using SizeScanner.Avalonia.ViewModels;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class ChartViewModelTests
{
    private sealed class NoopFs : IFileSystemActions
    {
        public void ShowInExplorer(string path) { }
        public Task<DeleteResult> DeleteAsync(string path, bool permanent) =>
            Task.FromResult(new DeleteResult(true, null));
    }

    private sealed class FailingFs : IFileSystemActions
    {
        public void ShowInExplorer(string path) { }
        public Task<DeleteResult> DeleteAsync(string path, bool permanent) =>
            Task.FromResult(new DeleteResult(false, "boom"));
    }

    private sealed class PendingFs : IFileSystemActions
    {
        public TaskCompletionSource<string> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DeleteResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ShowInExplorer(string path) { }

        public Task<DeleteResult> DeleteAsync(string path, bool permanent)
        {
            Started.TrySetResult(path);
            return Completion.Task;
        }
    }

    private sealed class NoopDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }

    private static ChartViewModel CreateVm(FakeScanService? scan = null) =>
        new(scan ?? new FakeScanService(), new NoopFs(), new NoopDialogs());

    private static ChartViewModel CreateVm(IFileSystemActions fileSystem, FakeScanService? scan = null) =>
        new(scan ?? new FakeScanService(), fileSystem, new NoopDialogs());

    private static FsItem SampleDriveRoot() =>
        TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 0),
            TestTree.Dir("Windows",
                TestTree.File("kernel.sys", 300)),
            TestTree.File("page.sys", 200));

    [Fact]
    public void SetScan_then_Refresh_populates_chart_layout()
    {
        var vm = CreateVm();
        vm.SetScan(SampleDriveRoot(), isDrive: true, targetPath: "C:\\");
        vm.Refresh(filterPercent: 0f, includeFreeSpace: false);

        Assert.NotEmpty(vm.Layout.Segments);
        Assert.False(vm.IsScoped);
    }

    [Fact]
    public void HideFreeSpace_removes_free_space_but_keeps_inaccessible()
    {
        var vm = CreateVm();
        var root = TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 50),
            TestTree.Dir("Windows",
                TestTree.File("kernel.sys", 300)),
            TestTree.File("page.sys", 200));

        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        Assert.DoesNotContain(vm.Layout.Segments, s => s.Node?.Name == DriveScanMetadata.FreeSpaceName);
        Assert.Contains(vm.Layout.Segments, s => s.Node?.Name == DriveScanMetadata.InaccessibleName);
    }

    [Fact]
    public async Task Scoping_into_directory_calls_scope_scan_with_full_path_and_updates_state()
    {
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        var windows = root.Items![2];
        scan.ScopeResult = _ => windows;

        Assert.True(await vm.TryScopeAtAsync(windows));
        Assert.True(vm.IsScoped);
        Assert.Contains("Windows", vm.ScopeLabel);
        Assert.Equal([("C:\\Windows", true)], scan.ScopeCalls);

        await vm.GoToRootCommand.ExecuteAsync(null);
        Assert.False(vm.IsScoped);
        Assert.Empty(scan.RootCalls);
    }

    [Fact]
    public async Task TryScopeAtAsync_does_not_scan_non_scopable_nodes()
    {
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: true);

        var freeSpace = root.Items![0];
        var file = root.Items![3];
        var aggregate = FsItem.CreateAggregate(50);

        Assert.False(await vm.TryScopeAtAsync(freeSpace));
        Assert.False(await vm.TryScopeAtAsync(file));
        Assert.False(await vm.TryScopeAtAsync(aggregate));
        Assert.Empty(scan.ScopeCalls);
    }

    [Fact]
    public async Task Scope_scan_keeps_previous_chart_visible_until_it_completes()
    {
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);
        var layoutBefore = vm.Layout;

        var windows = root.Items![2];
        var pending = new TaskCompletionSource<FsItem>();
        scan.PendingScope = pending;

        var scopeTask = vm.TryScopeAtAsync(windows);

        Assert.True(vm.IsScopeScanning);
        Assert.False(vm.IsScoped);
        Assert.Same(layoutBefore, vm.Layout);

        pending.SetResult(TestTree.Dir("Windows", TestTree.File("kernel.sys", 300)));
        Assert.True(await scopeTask);

        Assert.True(vm.IsScoped);
        Assert.False(vm.IsScopeScanning);
    }

    [Fact]
    public async Task Cancelled_scope_scan_leaves_chart_unchanged()
    {
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);
        var layoutBefore = vm.Layout;

        var windows = root.Items![2];
        var pending = new TaskCompletionSource<FsItem>();
        scan.PendingScope = pending;

        var scopeTask = vm.TryScopeAtAsync(windows);
        vm.CancelScopeScan();
        pending.SetCanceled();

        Assert.False(await scopeTask);
        Assert.False(vm.IsScoped);
        Assert.False(vm.IsScopeScanning);
        Assert.Same(layoutBefore, vm.Layout);
    }

    [Fact]
    public async Task Second_scope_replaces_first_scoped_tree_rather_than_retaining_both()
    {
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        var windows = root.Items![2];
        var firstScope = TestTree.Dir("Windows",
            TestTree.Dir("System32", TestTree.File("a.dll", 10)));
        scan.ScopeResult = _ => firstScope;
        Assert.True(await vm.TryScopeAtAsync(windows));

        var system32 = firstScope.Items![0];
        var secondScope = TestTree.Dir("System32", TestTree.File("b.dll", 20));
        scan.ScopeResult = _ => secondScope;
        Assert.True(await vm.TryScopeAtAsync(system32));

        Assert.Contains(vm.Layout.Segments, s => s.Node?.Name == "b.dll");
        Assert.DoesNotContain(vm.Layout.Segments, s => s.Node?.Name == "a.dll");
        Assert.Equal([("C:\\Windows", true), ("C:\\Windows\\System32", true)], scan.ScopeCalls);
    }

    [Fact]
    public async Task GoUpAsync_rescans_the_parent_directory()
    {
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = TestTree.Dir("C:\\",
            TestTree.Dir("A",
                TestTree.Dir("B", TestTree.File("f.bin", 10))));
        vm.SetScan(root, isDrive: false, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        var a = root.Items![0];
        var b = a.Items![0];

        var rescannedParent = TestTree.Dir("A", TestTree.Dir("B2", TestTree.File("g.bin", 5)));
        scan.ScopeResult = target => target == "C:\\A\\B" ? b : rescannedParent;
        await vm.TryScopeAtAsync(b);

        await vm.GoUpCommand.ExecuteAsync(null);

        Assert.Equal([("C:\\A\\B", false), ("C:\\A", false)], scan.ScopeCalls);
        Assert.True(vm.IsScoped);
        Assert.Contains(vm.Layout.Segments, s => s.Node?.Name == "B2");
    }

    [Fact]
    public async Task GoUpAsync_clears_scope_without_rescanning_when_parent_is_cached_root()
    {
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = TestTree.Dir("C:\\",
            TestTree.Dir("A", TestTree.File("f.bin", 10)));
        vm.SetScan(root, isDrive: false, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        var a = root.Items![0];
        scan.ScopeResult = _ => a;
        await vm.TryScopeAtAsync(a);

        await vm.GoUpCommand.ExecuteAsync(null);

        Assert.False(vm.IsScoped);
        Assert.Equal([("C:\\A", false)], scan.ScopeCalls);
    }

    [Fact]
    public async Task GoUpAsync_to_cached_root_rescans_when_root_is_stale()
    {
        // Repro for the GoUpAsync stale-root bug: a delete inside a scoped
        // subtree marks the cached root stale, and going up to that cached
        // root (rather than "Go to root") must still trigger a rescan instead
        // of silently showing the stale tree with the deleted item still gone
        // from the scope but present in the unrescanned root.
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = TestTree.Dir("C:\\",
            TestTree.Dir("Users",
                TestTree.File("profile.dat", 300)),
            TestTree.File("page.sys", 200));
        vm.SetScan(root, isDrive: false, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        var users = root.Items![0];
        var scopedUsers = TestTree.Dir("Users", TestTree.File("profile.dat", 300));
        scan.ScopeResult = _ => scopedUsers;
        await vm.TryScopeAtAsync(users);

        var scopedProfile = scopedUsers.Items![0];
        vm.SetContextTarget(scopedProfile);
        await vm.DeleteCommand.ExecuteAsync(null);

        // The un-rescanned root tree must not be mutated by a scoped-tree delete.
        Assert.Equal(500, root.Size);

        var rescannedRoot = TestTree.Dir("C:\\", TestTree.File("page.sys", 200));
        scan.RootResult = (_, _) => rescannedRoot;
        FsItem? rescanned = null;
        vm.RootRescanned += r => rescanned = r;

        await vm.GoUpCommand.ExecuteAsync(null);

        Assert.False(vm.IsScoped);
        Assert.Same(rescannedRoot, rescanned);
        Assert.Equal([("C:\\", false)], scan.RootCalls);
    }

    [Fact]
    public async Task Delete_in_scoped_tree_marks_root_stale_and_GoToRoot_rescans_it()
    {
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = TestTree.Dir("C:\\",
            TestTree.Dir("Users",
                TestTree.File("profile.dat", 300)),
            TestTree.File("page.sys", 200));
        vm.SetScan(root, isDrive: false, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        var users = root.Items![0];
        var scopedUsers = TestTree.Dir("Users", TestTree.File("profile.dat", 300));
        scan.ScopeResult = _ => scopedUsers;
        await vm.TryScopeAtAsync(users);

        var scopedProfile = scopedUsers.Items![0];
        vm.SetContextTarget(scopedProfile);
        await vm.DeleteCommand.ExecuteAsync(null);

        // The un-rescanned root tree must not be mutated by a scoped-tree delete.
        Assert.Equal(500, root.Size);

        var rescannedRoot = TestTree.Dir("C:\\", TestTree.File("page.sys", 200));
        scan.RootResult = (_, _) => rescannedRoot;
        FsItem? rescanned = null;
        vm.RootRescanned += r => rescanned = r;

        await vm.GoToRootCommand.ExecuteAsync(null);

        Assert.False(vm.IsScoped);
        Assert.Same(rescannedRoot, rescanned);
        Assert.Equal([("C:\\", false)], scan.RootCalls);
    }

    [Fact]
    public void SuppressesContextMenu_for_synthetic_root_segments()
    {
        var vm = CreateVm();
        var filtered = new FsItem(ChartDisplayMetadata.FilteredName, 5, isDir: false);
        var freeSpace = new FsItem(DriveScanMetadata.FreeSpaceName, 500, isDir: false);
        var inaccessible = new FsItem(DriveScanMetadata.InaccessibleName, 0, isDir: false);
        var folder = new FsItem("Windows", 100, isDir: true) { Items = [] };

        Assert.True(vm.SuppressesContextMenu(filtered));
        Assert.True(vm.SuppressesContextMenu(freeSpace));
        Assert.True(vm.SuppressesContextMenu(inaccessible));
        Assert.True(vm.SuppressesContextMenu(null));
        Assert.False(vm.SuppressesContextMenu(folder));
    }

    [Fact]
    public async Task CannotScope_into_free_space_or_files()
    {
        var vm = CreateVm();
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: true);

        var freeSpace = root.Items![0];
        var file = root.Items![3];
        Assert.False(await vm.TryScopeAtAsync(freeSpace));
        Assert.False(await vm.TryScopeAtAsync(file));
    }

    [Fact]
    public void CannotScope_into_scanner_aggregate()
    {
        var vm = CreateVm();
        var aggregate = FsItem.CreateAggregate(50);

        Assert.False(vm.CanScopeTo(aggregate));
    }

    [Fact]
    public void CanScope_into_directory_with_only_unretained_children()
    {
        var vm = CreateVm();
        var dir = TestTree.DirWithUnretainedChildren("bounded", 500);

        Assert.True(vm.CanScopeTo(dir));
    }

    [Fact]
    public void Hover_builds_status_path_and_tooltip()
    {
        var vm = CreateVm();
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        var kernel = root.Items![2].Items![0]; // Windows/kernel.sys
        vm.Hover(kernel);

        Assert.Contains("kernel.sys", vm.HoverPath);
        Assert.Contains("Windows", vm.HoverToolTip);
        Assert.Contains("` kernel.sys", vm.HoverToolTip);

        vm.ClearHover();
        Assert.Equal(string.Empty, vm.HoverPath);
        Assert.Equal(string.Empty, vm.HoverToolTip);
    }

    [Fact]
    public void Hover_unscoped_drive_without_free_space_excludes_drive_root()
    {
        var vm = CreateVm();
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        var kernel = root.Items![2].Items![0]; // Windows/kernel.sys
        vm.Hover(kernel);

        // The chart root (drive root, synthetic entries stripped) is the path
        // prefix, so the chain/tooltip must start below it at "Windows" and must
        // not re-include the drive root.
        var lines = vm.HoverToolTip.Split(System.Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Windows", lines[0]);
        Assert.Contains("` kernel.sys", lines[1]);
        Assert.Equal("C:\\Windows\\kernel.sys", vm.HoverPath);
    }

    [Fact]
    public async Task Scoping_recomputes_filter_threshold_from_display_root()
    {
        var scan = new FakeScanService();
        var vm = CreateVm(scan);
        var root = TestTree.Dir("C:\\",
            TestTree.File("huge", 100_000),
            TestTree.Dir("target",
                TestTree.File("big", 400),
                TestTree.File("medium", 100),
                TestTree.File("tiny", 5)));

        vm.SetScan(root, isDrive: false, targetPath: "C:\\");
        const float filterPercent = 0.01f;
        vm.Refresh(filterPercent, includeFreeSpace: false);

        var filteredAtRoot = Assert.Single(
            vm.Layout.Segments,
            s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(505, filteredAtRoot.Size);
        Assert.DoesNotContain(vm.Layout.Segments, s => s.Node?.Name == "big");

        var target = root.Items![1];
        scan.ScopeResult = _ => target;
        Assert.True(await vm.TryScopeAtAsync(target));

        filteredAtRoot = Assert.Single(
            vm.Layout.Segments,
            s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(5, filteredAtRoot.Size);
        Assert.Contains(vm.Layout.Segments, s => s.Node?.Name == "big");
        Assert.Contains(vm.Layout.Segments, s => s.Node?.Name == "medium");

        await vm.GoToRootCommand.ExecuteAsync(null);

        filteredAtRoot = Assert.Single(
            vm.Layout.Segments,
            s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(505, filteredAtRoot.Size);
    }

    [Fact]
    public async Task DeleteCommand_success_removes_node_from_chart_and_updates_sizes()
    {
        var vm = CreateVm();
        var root = TestTree.Dir("C:\\",
            TestTree.Dir("Users",
                TestTree.File("profile.dat", 300)),
            TestTree.File("page.sys", 200));
        var users = root.Items![0];

        vm.SetScan(root, isDrive: false, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);
        vm.SetContextTarget(users);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Layout.Segments, s => s.Node?.Name == "Users");
        Assert.DoesNotContain(root.Items!, item => ReferenceEquals(item, users));
        Assert.Equal(200, root.Size);
        Assert.Null(vm.ContextTarget);
        Assert.Equal(string.Empty, vm.ContextTargetPath);
    }

    [Fact]
    public async Task DeleteCommand_failure_leaves_chart_unchanged()
    {
        var vm = CreateVm(new FailingFs());
        var root = TestTree.Dir("C:\\",
            TestTree.Dir("Users",
                TestTree.File("profile.dat", 300)),
            TestTree.File("page.sys", 200));
        var users = root.Items![0];

        vm.SetScan(root, isDrive: false, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);
        vm.SetContextTarget(users);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Contains(vm.Layout.Segments, s => s.Node?.Name == "Users");
        Assert.Contains(root.Items!, item => ReferenceEquals(item, users));
        Assert.Equal(500, root.Size);
    }

    [Fact]
    public async Task DeleteCommand_top_level_drive_child_updates_hidden_free_space_chart_root()
    {
        var vm = CreateVm();
        var root = TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 0),
            TestTree.Dir("Windows",
                TestTree.File("kernel.sys", 300)),
            TestTree.File("page.sys", 200));
        var windows = root.Items![2];

        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);
        vm.SetContextTarget(windows);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Layout.Segments, s => s.Node?.Name == "Windows");
        Assert.Contains(vm.Layout.Segments, s => s.Node?.Name == "page.sys");
        Assert.DoesNotContain(root.Items!, item => ReferenceEquals(item, windows));
        Assert.Equal(700, root.Size);
    }

    [Fact]
    public async Task DeleteCommand_updates_delete_status_while_operation_is_running()
    {
        var fileSystem = new PendingFs();
        var vm = CreateVm(fileSystem);
        var root = TestTree.Dir("C:\\",
            TestTree.File("large.bin", 300));
        var file = root.Items![0];

        vm.SetScan(root, isDrive: false, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);
        vm.SetContextTarget(file);

        var deleteTask = vm.DeleteCommand.ExecuteAsync(null);
        await fileSystem.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(vm.IsDeleting);
        Assert.Contains("Moving to Recycle Bin", vm.DeleteStatusText);
        Assert.Contains("large.bin", vm.DeleteStatusText);

        fileSystem.Completion.SetResult(new DeleteResult(true, null));
        await deleteTask;

        Assert.False(vm.IsDeleting);
        Assert.Equal(string.Empty, vm.DeleteStatusText);
    }
}
