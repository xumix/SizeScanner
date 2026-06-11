// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class DriveScannerTests
{
    [Fact]
    public void ScanDirectory_sums_file_sizes()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("a.txt", 100);
        temp.CreateFile("sub/b.txt", 250);

        var scanner = new DriveScanner();
        var root = scanner.ScanDirectory(temp.Path, CancellationToken.None);

        Assert.Equal(350, root.Size);
        Assert.Equal(2, root.Items!.Count);
    }

    [Fact]
    public void GetDisplayThreshold_returns_zero_for_directory_scan_when_free_space_is_excluded()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("big.bin", 1000);

        var scanner = new DriveScanner();
        scanner.ScanDirectory(temp.Path, CancellationToken.None);

        var threshold = scanner.GetDisplayThreshold(0.1f, includeFreeSpace: false);
        Assert.Equal(0, threshold);
    }

    [Fact]
    public void GetDisplayThreshold_uses_scanned_total_for_directory_scan_when_free_space_is_included()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("big.bin", 1000);

        var scanner = new DriveScanner();
        scanner.ScanDirectory(temp.Path, CancellationToken.None);

        var threshold = scanner.GetDisplayThreshold(0.1f, includeFreeSpace: true);
        Assert.Equal(100, threshold);
    }

    [Fact]
    public void ScanDirectory_sets_current_target_to_requested_path()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("ok.txt", 10);

        var scanner = new DriveScanner();
        scanner.ScanDirectory(temp.Path, CancellationToken.None);

        Assert.Equal(temp.Path, scanner.CurrentTarget);
    }

    [Fact]
    public void Inaccessible_starts_empty_for_readable_tree()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("ok.txt", 10);

        var scanner = new DriveScanner();
        scanner.ScanDirectory(temp.Path, CancellationToken.None);

        Assert.Empty(scanner.Inaccessible);
    }
}
