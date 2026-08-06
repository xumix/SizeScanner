// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class BoundedDirectoryWalkerTests
{
    [Fact]
    public void Million_file_directory_keeps_exact_size_with_bounded_tree()
    {
        var source = SyntheticSources.WideDirectory(
            @"C:\wide", 1_000_000, size: 4);
        var budget = new ScanTreeBudget(
            maxRetainedNodes: 103,
            maxChildrenPerDirectory: 99,
            maxRetainedDepth: 6);

        var result = new BoundedDirectoryWalker(source).Scan(
            @"C:\wide", budget, CancellationToken.None, null);

        Assert.Equal(4_000_000, result.Total);
        Assert.Equal(4_000_000, result.Root.Size);
        Assert.True(result.Root.CountRetainedNodes() <= 103);
        Assert.Contains(result.Root.Items!, item => item.IsAggregate);
    }

    [Fact]
    public void Depth_limit_keeps_size_and_marks_directory_as_truncated()
    {
        using var temp = new TemporaryDirectory();
        var relative = string.Join(
            Path.DirectorySeparatorChar,
            Enumerable.Range(0, 20).Select(index => $"d{index}"));
        temp.CreateFile(Path.Combine(relative, "leaf.bin"), 7);
        var source = new DirectoryScanner(preferAllocatedSize: false);
        var budget = new ScanTreeBudget(
            maxRetainedNodes: 100,
            maxRetainedDepth: 3);

        var result = new BoundedDirectoryWalker(source).Scan(
            temp.Path, budget, CancellationToken.None, null);

        Assert.Equal(7, result.Total);
        Assert.True(MaxDepth(result.Root) <= 4);
        Assert.True(DeepestDirectory(result.Root).HasUnretainedChildren);
    }

    [Fact]
    public void Missing_directory_source_marks_root_inaccessible()
    {
        var source = new SyntheticDirectoryEntrySource(
            @"C:\other", 0, 0, false);
        var budget = ScanTreeBudget.Default;

        var result = new BoundedDirectoryWalker(source).Scan(
            @"C:\missing", budget, CancellationToken.None, null);

        Assert.Null(result.Root.Items);
        Assert.Equal(1, result.InaccessibleCount);
        Assert.Contains(@"C:\missing", result.Inaccessible);
        Assert.False(result.InaccessiblePathsTruncated);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void Inaccessible_path_samples_are_capped_but_count_is_exact()
    {
        var source = SyntheticSources.WideDirectory(
            @"C:\root", 20, size: 0, directories: true);
        var budget = new ScanTreeBudget(
            maxRetainedNodes: 1_000,
            maxChildrenPerDirectory: 99,
            maxRetainedDepth: 6,
            maxInaccessiblePaths: 5);

        var result = new BoundedDirectoryWalker(source).Scan(
            @"C:\root", budget, CancellationToken.None, null);

        Assert.Equal(20, result.InaccessibleCount);
        Assert.Equal(5, result.Inaccessible.Count);
        Assert.True(result.InaccessiblePathsTruncated);
    }

    [Fact]
    public void Cancellation_throws_and_does_not_publish_partial_scan()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("a.bin", 1);
        var source = new DirectoryScanner(preferAllocatedSize: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new BoundedDirectoryWalker(source).Scan(
                temp.Path, ScanTreeBudget.Default, cts.Token, null));
    }

    private static int MaxDepth(FsItem item) =>
        item.Items is not { Count: > 0 }
            ? 1
            : 1 + item.Items.Max(MaxDepth);

    private static FsItem DeepestDirectory(FsItem item)
    {
        var current = item;
        while (current.Items?.FirstOrDefault(
            child => child.IsDir) is { } child)
            current = child;
        return current;
    }
}
