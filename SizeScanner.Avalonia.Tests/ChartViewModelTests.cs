// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

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
        public bool TryDelete(string path, bool permanent, out string? error) { error = null; return true; }
    }

    private sealed class NoopDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }

    private static ChartViewModel CreateVm() => new(new NoopFs(), new NoopDialogs());

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
    public void Scoping_into_directory_updates_scope_state()
    {
        var vm = CreateVm();
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: false);

        var windows = root.Items![2];
        Assert.True(vm.TryScopeAt(windows));
        Assert.True(vm.IsScoped);
        Assert.Contains("Windows", vm.ScopeLabel);

        vm.GoToRootCommand.Execute(null);
        Assert.False(vm.IsScoped);
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
    public void CannotScope_into_free_space_or_files()
    {
        var vm = CreateVm();
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0f, includeFreeSpace: true);

        var freeSpace = root.Items![0];
        var file = root.Items![3];
        Assert.False(vm.TryScopeAt(freeSpace));
        Assert.False(vm.TryScopeAt(file));
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
    public void Scoping_recomputes_filter_threshold_from_display_root()
    {
        var vm = CreateVm();
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
        Assert.True(vm.TryScopeAt(target));

        filteredAtRoot = Assert.Single(
            vm.Layout.Segments,
            s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(5, filteredAtRoot.Size);
        Assert.Contains(vm.Layout.Segments, s => s.Node?.Name == "big");
        Assert.Contains(vm.Layout.Segments, s => s.Node?.Name == "medium");

        vm.GoToRootCommand.Execute(null);

        filteredAtRoot = Assert.Single(
            vm.Layout.Segments,
            s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(505, filteredAtRoot.Size);
    }
}
