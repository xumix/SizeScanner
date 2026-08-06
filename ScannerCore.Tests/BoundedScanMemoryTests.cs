// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Diagnostics;
using System.Threading;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class BoundedScanMemoryTests
{
    [Fact]
    public void Retained_node_count_is_constant_as_input_grows()
    {
        var budget = new ScanTreeBudget(
            maxRetainedNodes: 1_000,
            maxChildrenPerDirectory: 99,
            maxRetainedDepth: 6);

        var small = ScanSyntheticFiles(1_000, budget);
        var large = ScanSyntheticFiles(1_000_000, budget);

        Assert.True(
            small.Root.CountRetainedNodes()
            <= budget.MaxRetainedNodes);
        Assert.True(
            large.Root.CountRetainedNodes()
            <= budget.MaxRetainedNodes);
        Assert.Equal(1_000_000, large.Total);
    }

    [Fact]
    public void Inaccessible_samples_are_bounded()
    {
        var source = SyntheticSources.WideDirectory(
            @"C:\denied", 1_000_000, size: 0,
            directories: true);
        var budget = new ScanTreeBudget(
            maxRetainedNodes: 1_000,
            maxInaccessiblePaths: 32);

        var result = new BoundedDirectoryWalker(source).Scan(
            @"C:\denied", budget, CancellationToken.None, null);

        Assert.Equal(1_000_000, result.InaccessibleCount);
        Assert.Equal(32, result.Inaccessible.Count);
        Assert.True(result.InaccessiblePathsTruncated);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Ten_million_entries_stay_within_bounded_memory()
    {
        if (Environment.GetEnvironmentVariable("SIZESCANNER_RUN_PERF_TESTS") != "1")
            return;

        var budget = new ScanTreeBudget(
            maxRetainedNodes: 1_000,
            maxChildrenPerDirectory: 99,
            maxRetainedDepth: 6);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();

        var result = ScanSyntheticFiles(10_000_000, budget);

        stopwatch.Stop();
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var heapBytes = GC.GetTotalMemory(forceFullCollection: true);
        var retainedNodes = result.Root.CountRetainedNodes();

        Console.WriteLine(
            $"Elapsed: {stopwatch.Elapsed}, " +
            $"Total: {result.Total:N0}, " +
            $"Allocated: {(allocatedAfter - allocatedBefore):N0} bytes, " +
            $"Heap: {heapBytes:N0} bytes, " +
            $"Retained nodes: {retainedNodes:N0}");

        Assert.Equal(10_000_000, result.Total);
        Assert.True(retainedNodes <= budget.MaxRetainedNodes);
    }

    private static ScanResult ScanSyntheticFiles(
        int fileCount,
        ScanTreeBudget budget)
    {
        var source = SyntheticSources.WideDirectory(
            @"C:\memory-test", fileCount, size: 1);
        return new BoundedDirectoryWalker(source).Scan(
            @"C:\memory-test", budget, CancellationToken.None, null);
    }
}
