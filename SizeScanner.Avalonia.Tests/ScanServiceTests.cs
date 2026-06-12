// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
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
}
