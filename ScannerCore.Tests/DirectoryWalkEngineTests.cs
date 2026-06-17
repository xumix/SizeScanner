// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using System.Threading;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class DirectoryWalkEngineTests
{
    [Fact]
    public void Scan_builds_tree_with_sizes_parents_and_total()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("a.txt", 100);
        temp.CreateFile("sub/b.txt", 250);

        var engine = new DirectoryWalkEngine();
        var result = engine.Scan(temp.Path, isDriveScan: false, CancellationToken.None, onProgress: null);

        Assert.Equal(350, result.Root.Size);
        Assert.Equal(350, result.Total);
        Assert.Equal(2, result.Root.Items!.Count);
        var sub = result.Root.Items!.Single(i => i.Name == "sub");
        Assert.Same(result.Root, sub.Parent);
        Assert.Empty(result.Inaccessible);
    }

    [Fact]
    public void CanHandle_is_always_true()
    {
        Assert.True(new DirectoryWalkEngine().CanHandle("C:", isDriveScan: true, isElevated: false));
        Assert.True(new DirectoryWalkEngine().CanHandle(@"C:\x", isDriveScan: false, isElevated: true));
    }

    [Fact]
    public void Scan_reports_progress_paths()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("sub/b.txt", 10);

        var paths = new System.Collections.Concurrent.ConcurrentBag<string>();
        new DirectoryWalkEngine().Scan(temp.Path, isDriveScan: false, CancellationToken.None,
            (path, _) => paths.Add(path));

        Assert.Contains(paths, p => p.Contains("sub"));
    }
}
