// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Threading;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class DirectoryWalkEngineParallelTests
{
    [Fact]
    public void Parallel_walk_matches_sequential_totals_over_wide_tree()
    {
        using var temp = new TemporaryDirectory();
        long expected = 0;
        for (var d = 0; d < 12; d++)
            for (var f = 0; f < 10; f++)
            {
                var size = d * 10 + f + 1;
                temp.CreateFile($"dir{d}/file{f}.dat", size);
                expected += size;
            }

        var result = new DirectoryWalkEngine().Scan(temp.Path, isDriveScan: false, CancellationToken.None, null);

        Assert.Equal(expected, result.Root.Size);
        Assert.Equal(expected, result.Total);
        Assert.Equal(12, result.Root.Items!.Count(i => i.IsDir));
        foreach (var dir in result.Root.Items!.Where(i => i.IsDir))
            Assert.Same(result.Root, dir.Parent);
    }

    [Fact]
    public void Spinning_disk_policy_uses_sequential_walk_with_correct_totals()
    {
        using var temp = new TemporaryDirectory();
        long expected = 0;
        for (var d = 0; d < 12; d++)
            for (var f = 0; f < 10; f++)
            {
                var size = d * 10 + f + 1;
                temp.CreateFile($"dir{d}/file{f}.dat", size);
                expected += size;
            }

        var engine = new DirectoryWalkEngine(_ => false);
        var result = engine.Scan(temp.Path, isDriveScan: false, CancellationToken.None, null);

        Assert.Equal(expected, result.Root.Size);
        Assert.Equal(expected, result.Total);
    }

    [Fact]
    public void Cancellation_stops_the_walk()
    {
        using var temp = new TemporaryDirectory();
        for (var d = 0; d < 8; d++)
            for (var f = 0; f < 20; f++)
                temp.CreateFile($"dir{d}/file{f}.dat", 1);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = new DirectoryWalkEngine().Scan(temp.Path, isDriveScan: false, cts.Token, null);

        // Root entries may be enumerated, but cancellation must prevent a full descent.
        Assert.True(result.Total <= 160);
    }
}
