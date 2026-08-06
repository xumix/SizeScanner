// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScannerCore;
using SizeScanner.Avalonia.Services;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class ScanServiceTests
{
    [Fact]
    public async Task RunAsync_directory_scan_builds_tree()
    {
        using var dir = new TempDir();
        dir.CreateFile("a.bin", 2048);
        dir.CreateFile("sub\\b.bin", 1024);

        var service = new ScanService();
        var progress = new Progress<ScanProgress>(_ => { });

        var root = await service.RunAsync(dir.Path, isDrive: false, CancellationToken.None, progress);

        Assert.False(service.IsDriveScan);
        Assert.Equal(dir.Path, service.LastTarget);
        Assert.NotNull(root.Items);
        Assert.True(root.Size >= 3072);
    }

    [Fact]
    public async Task RunAsync_honors_cancellation()
    {
        using var dir = new TempDir();
        dir.CreateFile("a.bin", 16);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = new ScanService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync(dir.Path, isDrive: false, cts.Token, new Progress<ScanProgress>(_ => { })));
    }

    [Fact]
    public async Task RunScopeAsync_builds_tree_without_mutating_root_scan_state()
    {
        using var dir = new TempDir();
        dir.CreateFile("a.bin", 2048);
        dir.CreateFile("sub\\b.bin", 1024);

        using var rootDir = new TempDir();
        rootDir.CreateFile("root.bin", 4096);

        var service = new ScanService();
        await service.RunAsync(rootDir.Path, isDrive: false, CancellationToken.None, new Progress<ScanProgress>(_ => { }));
        var rootScanner = service.Scanner;

        var scoped = await service.RunScopeAsync(dir.Path, CancellationToken.None, new Progress<ScanProgress>(_ => { }));

        Assert.NotNull(scoped.Items);
        Assert.True(scoped.Size >= 3072);
        Assert.Equal(rootDir.Path, service.LastTarget);
        Assert.False(service.IsDriveScan);
        Assert.Same(rootScanner, service.Scanner);
    }

    [Fact]
    public async Task RunScopeAsync_honors_cancellation()
    {
        using var dir = new TempDir();
        dir.CreateFile("a.bin", 16);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = new ScanService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunScopeAsync(dir.Path, cts.Token, new Progress<ScanProgress>(_ => { })));
    }

    [Fact]
    public async Task RunScopeAsync_preferAllocatedSize_requests_allocation_size_not_logical_size()
    {
        // A scope rescan under a drive-rooted tree must preserve allocation-size
        // semantics (global rule: drive scans use allocation size, directory scans
        // use logical size). A 1-byte file's logical size is always 1, but its
        // on-disk allocation is rounded up to at least a filesystem cluster.
        using var dir = new TempDir();
        dir.CreateFile("tiny.bin", 1);

        var service = new ScanService();
        var progress = new Progress<ScanProgress>(_ => { });

        var logical = await service.RunScopeAsync(dir.Path, CancellationToken.None, progress, preferAllocatedSize: false);
        var allocated = await service.RunScopeAsync(dir.Path, CancellationToken.None, progress, preferAllocatedSize: true);

        var logicalSize = logical.Items!.Single(i => i.Name == "tiny.bin").Size;
        var allocatedSize = allocated.Items!.Single(i => i.Name == "tiny.bin").Size;

        Assert.Equal(1, logicalSize);
        Assert.True(
            allocatedSize > logicalSize,
            $"Expected allocation size ({allocatedSize}) to exceed logical size ({logicalSize}) for a 1-byte file.");
    }
}
