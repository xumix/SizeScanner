// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
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
    private sealed class FakeScan : IScanService
    {
        private readonly FsItem _root;
        public FakeScan(FsItem root) => _root = root;
        public string LastTarget { get; private set; } = string.Empty;
        public bool IsDriveScan { get; private set; }
        public DriveScanner Scanner { get; } = new();
        public Task<FsItem> RunAsync(string target, bool isDrive, CancellationToken ct, IProgress<ScanProgress> p)
        {
            LastTarget = target;
            IsDriveScan = isDrive;
            return Task.FromResult(_root);
        }
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public UserSettings Saved { get; private set; } = new();
        public UserSettings Load() => new();
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
        public bool TryDelete(string path, bool permanent, out string? error) { error = null; return true; }
    }

    private sealed class NoopDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }

    private static MainWindowViewModel CreateVm(FsItem root, FakeSettings? settings = null) =>
        new(new FakeScan(root), settings ?? new FakeSettings(), new FakeDrives(),
            new FakeElevation(), new FakeFolderPicker(),
            new ChartViewModel(new NoopFs(), new NoopDialogs()));

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
        Assert.False(vm.IsScanning);
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
}
