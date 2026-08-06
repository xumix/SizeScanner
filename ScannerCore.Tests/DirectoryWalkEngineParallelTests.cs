// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
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

        var result = new DirectoryWalkEngine().Scan(temp.Path, isDriveScan: false, CancellationToken.None, null, ScanTreeBudget.Default);

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
        var result = engine.Scan(temp.Path, isDriveScan: false, CancellationToken.None, null, ScanTreeBudget.Default);

        Assert.Equal(expected, result.Root.Size);
        Assert.Equal(expected, result.Total);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cancellation_throws_before_publishing_a_partial_scan(bool parallelizeTopLevel)
    {
        using var temp = new TemporaryDirectory();
        for (var d = 0; d < 8; d++)
            for (var f = 0; f < 20; f++)
                temp.CreateFile($"dir{d}/file{f}.dat", 1);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var engine = new DirectoryWalkEngine(_ => parallelizeTopLevel);

        Assert.Throws<OperationCanceledException>(() =>
            engine.Scan(temp.Path, isDriveScan: false, cts.Token, null, ScanTreeBudget.Default));
    }

    [Fact]
    public void Parallel_and_sequential_walks_are_equivalent_and_bounded()
    {
        using var temp = new TemporaryDirectory();
        for (var d = 0; d < 12; d++)
            for (var f = 0; f < 10; f++)
                temp.CreateFile($"dir{d}/file{f}.dat", d * 10 + f + 1);

        var budget = new ScanTreeBudget(maxDegreeOfParallelism: 3);

        var maxConcurrentOpens = 0;
        var currentOpens = 0;
        var gate = new object();
        void TrackOpen(bool opened)
        {
            lock (gate)
            {
                currentOpens += opened ? 1 : -1;
                maxConcurrentOpens = Math.Max(maxConcurrentOpens, currentOpens);
            }
        }

        var trackedSource = new ConcurrencyTrackingSource(
            new DirectoryScanner(preferAllocatedSize: false), temp.Path, TrackOpen);

        var sequential = new BoundedDirectoryWalker(new DirectoryScanner(preferAllocatedSize: false), parallelizeTopLevel: false)
            .Scan(temp.Path, budget, CancellationToken.None, null);
        var parallel = new BoundedDirectoryWalker(trackedSource, parallelizeTopLevel: true)
            .Scan(temp.Path, budget, CancellationToken.None, null);

        Assert.Equal(sequential.Total, parallel.Total);
        Assert.Equal(
            sequential.Root.Items!.Select(x => (x.Name, x.Size)),
            parallel.Root.Items!.Select(x => (x.Name, x.Size)));
        Assert.True(parallel.Root.CountRetainedNodes() <= budget.MaxRetainedNodes);
        Assert.True(maxConcurrentOpens <= budget.MaxDegreeOfParallelism);
    }

    private sealed class ConcurrencyTrackingSource(
        IDirectoryEntrySource inner,
        string untrackedRoot,
        Action<bool> onOpenChanged) : IDirectoryEntrySource
    {
        public IDirectoryEntryCursor? Open(string path)
        {
            var cursor = inner.Open(path);
            if (cursor is null)
                return null;

            // The root's own cursor stays open for the whole parallel fan-out (it is
            // disposed only after every worker completes); excluding it isolates the
            // metric to actual worker concurrency, which is what MaxDegreeOfParallelism
            // bounds.
            if (path.TrimEnd(System.IO.Path.DirectorySeparatorChar)
                .Equals(untrackedRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                return cursor;

            onOpenChanged(true);
            Thread.Sleep(5); // widen the window so overlapping opens are observable.
            return new TrackingCursor(cursor, onOpenChanged);
        }

        private sealed class TrackingCursor(
            IDirectoryEntryCursor inner,
            Action<bool> onOpenChanged) : IDirectoryEntryCursor
        {
            private bool _disposed;

            public DirectoryBatchResult ReadNext(Span<byte> buffer, IDirectoryEntrySink sink) =>
                inner.ReadNext(buffer, sink);

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                inner.Dispose();
                onOpenChanged(false);
            }
        }
    }
}
