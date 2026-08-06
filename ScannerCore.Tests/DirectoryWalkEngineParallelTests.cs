// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public void Synthetic_tree_source_walks_a_nested_tree()
    {
        var tree = SyntheticNode.Dir("root",
            SyntheticNode.Dir("a",
                SyntheticNode.File("f1", 10),
                SyntheticNode.Dir("b", SyntheticNode.File("f2", 20))),
            SyntheticNode.File("f3", 5));
        var source = new SyntheticTreeSource(@"C:\root", tree);

        var result = new BoundedDirectoryWalker(source).Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 1),
            CancellationToken.None,
            null);

        Assert.Equal(35, result.Total);
        Assert.Equal(35, result.Root.Size);
        Assert.Equal(0, result.InaccessibleCount);
        Assert.True(source.Probe.Peak >= 1);
    }

    [Fact]
    public void Parallel_and_sequential_walks_are_equivalent_and_bounded()
    {
        using var temp = new TemporaryDirectory();
        for (var d = 0; d < 12; d++)
            for (var f = 0; f < 10; f++)
                temp.CreateFile($"dir{d}/file{f}.dat", d * 10 + f + 1);

        var budget = new ScanTreeBudget(maxDegreeOfParallelism: 3, parallelFanOutLevels: 1);

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

    private static SyntheticNode LongPoleTree() =>
        SyntheticNode.Dir("root",
            SyntheticNode.Dir("only",
                SyntheticNode.Dir("g1", SyntheticNode.File("a", 1)),
                SyntheticNode.Dir("g2", SyntheticNode.File("b", 2))));

    private static bool IsGrandchild(string path) =>
        path.EndsWith("g1", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("g2", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="onFirst"/> the first time each matching directory is read, so a
    /// rendezvous is not re-entered by a directory's trailing end-of-directory read.
    /// </summary>
    private static Action<string> GateFirstRead(
        Func<string, bool> match, Action onFirst)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return path =>
        {
            if (!match(path))
                return;
            lock (seen)
            {
                if (!seen.Add(path))
                    return;
            }

            onFirst();
        };
    }

    private static bool CompletesWithin(Task task, TimeSpan timeout) =>
        ReferenceEquals(
            Task.WhenAny(task, Task.Delay(timeout)).GetAwaiter().GetResult(),
            task);

    [Fact]
    public void Second_level_subtrees_are_walked_concurrently()
    {
        var rendezvous = new Rendezvous(participants: 2, TimeSpan.FromSeconds(3));
        var source = new SyntheticTreeSource(@"C:\root", LongPoleTree())
        {
            GateRead = GateFirstRead(IsGrandchild, rendezvous.Arrive)
        };

        var result = new BoundedDirectoryWalker(source, parallelizeTopLevel: true).Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 2, parallelFanOutLevels: 2),
            CancellationToken.None,
            null);

        Assert.Equal(3, result.Total);
        Assert.Equal(2, rendezvous.Peak);
    }

    [Fact]
    public void Concurrent_reads_never_exceed_the_configured_degree()
    {
        var children = new List<SyntheticNode>();
        for (var d = 0; d < 16; d++)
            children.Add(SyntheticNode.Dir(
                $"dir{d}",
                SyntheticNode.File("f", d + 1),
                SyntheticNode.Dir("nested", SyntheticNode.File("g", d + 1))));

        var source = new SyntheticTreeSource(@"C:\root", SyntheticNode.Dir("root", [.. children]))
        {
            GateRead = _ => Thread.Sleep(2)
        };

        new BoundedDirectoryWalker(source, parallelizeTopLevel: true).Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 3, parallelFanOutLevels: 2),
            CancellationToken.None,
            null);

        Assert.True(source.Probe.Peak <= 3, $"peak concurrent reads was {source.Probe.Peak}");
    }

    [Fact]
    public void Single_slot_fan_out_completes_without_deadlock()
    {
        var tree = SyntheticNode.Dir("root",
            SyntheticNode.Dir("a", SyntheticNode.File("f", 1)),
            SyntheticNode.Dir("b", SyntheticNode.File("f", 2)));
        var source = new SyntheticTreeSource(@"C:\root", tree);
        var walker = new BoundedDirectoryWalker(source, parallelizeTopLevel: true);

        var scan = Task.Run(() => walker.Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 1, parallelFanOutLevels: 2),
            CancellationToken.None,
            null));

        Assert.True(CompletesWithin(scan, TimeSpan.FromSeconds(15)), "scan did not complete");
        Assert.Equal(3, scan.Result.Total);
    }

    [Fact]
    public void Read_failure_in_one_subtree_aborts_the_scan_with_io_exception()
    {
        var tree = SyntheticNode.Dir("root",
            SyntheticNode.Dir("good", SyntheticNode.File("f", 1)),
            SyntheticNode.Dir("bad", SyntheticNode.File("f", 2)));
        var source = new SyntheticTreeSource(@"C:\root", tree)
        {
            FailPath = @"C:\root\bad"
        };
        var walker = new BoundedDirectoryWalker(source, parallelizeTopLevel: true);

        var scan = Task.Run(() => walker.Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 2, parallelFanOutLevels: 2),
            CancellationToken.None,
            null));

        Assert.True(CompletesWithin(scan, TimeSpan.FromSeconds(15)), "scan did not complete");
        Assert.IsType<IOException>(scan.Exception!.InnerException);
    }

    [Fact]
    public void Cancellation_during_fan_out_throws_operation_canceled()
    {
        var children = new List<SyntheticNode>();
        for (var d = 0; d < 32; d++)
            children.Add(SyntheticNode.Dir($"dir{d}", SyntheticNode.File("f", 1)));

        using var cts = new CancellationTokenSource();
        var source = new SyntheticTreeSource(@"C:\root", SyntheticNode.Dir("root", [.. children]))
        {
            GateRead = _ => cts.Cancel()
        };
        var walker = new BoundedDirectoryWalker(source, parallelizeTopLevel: true);

        var scan = Task.Run(() => walker.Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 2, parallelFanOutLevels: 2),
            cts.Token,
            null));

        Assert.True(CompletesWithin(scan, TimeSpan.FromSeconds(15)), "scan did not complete");
        Assert.IsAssignableFrom<OperationCanceledException>(scan.Exception!.InnerException);
    }

    /// <summary>
    /// Directly exercises Important Finding 1 from the Task 3 review: a faulted child
    /// must not let the scan return while its siblings are still reading. "good"
    /// directories sleep long enough in <c>GateRead</c> that, without sibling-abort and
    /// draining, they would still be mid-read when the faulted "bad" subtree's exception
    /// reaches the caller.
    /// </summary>
    [Fact]
    public void Failed_subtree_leaves_no_sibling_reads_active_once_the_scan_returns()
    {
        var children = new List<SyntheticNode>
        {
            SyntheticNode.Dir("bad", SyntheticNode.File("f", 1))
        };
        for (var d = 0; d < 6; d++)
            children.Add(SyntheticNode.Dir($"good{d}", SyntheticNode.File("f", d + 1)));

        var source = new SyntheticTreeSource(@"C:\root", SyntheticNode.Dir("root", [.. children]))
        {
            FailPath = @"C:\root\bad",
            GateRead = path =>
            {
                if (!path.EndsWith("bad", StringComparison.OrdinalIgnoreCase))
                    Thread.Sleep(300);
            }
        };
        var walker = new BoundedDirectoryWalker(source, parallelizeTopLevel: true);

        var scan = Task.Run(() => walker.Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 8, parallelFanOutLevels: 2),
            CancellationToken.None,
            null));

        Assert.True(CompletesWithin(scan, TimeSpan.FromSeconds(15)), "scan did not complete");
        Assert.IsType<IOException>(scan.Exception!.InnerException);
        Assert.Equal(0, source.Probe.Current);
    }

    [Fact]
    public void Outstanding_buffer_rentals_never_exceed_the_configured_degree()
    {
        var children = new List<SyntheticNode>();
        for (var d = 0; d < 16; d++)
            children.Add(SyntheticNode.Dir(
                $"dir{d}",
                SyntheticNode.File("f", d + 1),
                SyntheticNode.Dir("nested", SyntheticNode.File("g", d + 1))));

        var source = new SyntheticTreeSource(@"C:\root", SyntheticNode.Dir("root", [.. children]))
        {
            GateRead = _ => Thread.Sleep(2)
        };
        var pool = new TrackingArrayPool();
        var walker = new BoundedDirectoryWalker(source, parallelizeTopLevel: true) { BufferPool = pool };

        walker.Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 3, parallelFanOutLevels: 2),
            CancellationToken.None,
            null);

        Assert.True(pool.Peak <= 3, $"peak outstanding rentals was {pool.Peak}");
    }

    [Fact]
    public void Root_only_fan_out_leaves_second_level_subtrees_serialized()
    {
        var rendezvous = new Rendezvous(participants: 2, TimeSpan.FromMilliseconds(200));
        var source = new SyntheticTreeSource(@"C:\root", LongPoleTree())
        {
            GateRead = GateFirstRead(IsGrandchild, rendezvous.Arrive)
        };

        new BoundedDirectoryWalker(source, parallelizeTopLevel: true).Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 2, parallelFanOutLevels: 1),
            CancellationToken.None,
            null);

        Assert.Equal(1, rendezvous.Peak);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    public void Retained_tree_is_identical_across_fan_out_configurations(
        int levels, int degree)
    {
        var children = new List<SyntheticNode>();
        for (var d = 0; d < 8; d++)
        {
            var grandchildren = new List<SyntheticNode>();
            for (var g = 0; g < 5; g++)
                grandchildren.Add(SyntheticNode.Dir(
                    $"g{g}",
                    SyntheticNode.File("leaf", (d * 5) + g + 1)));
            children.Add(SyntheticNode.Dir($"dir{d}", [.. grandchildren]));
        }

        var tree = SyntheticNode.Dir("root", [.. children]);
        var budget = new ScanTreeBudget(
            maxRetainedNodes: 500,
            maxChildrenPerDirectory: 4,
            maxRetainedDepth: 6,
            maxDegreeOfParallelism: degree,
            parallelFanOutLevels: levels);

        var expected = new BoundedDirectoryWalker(
                new SyntheticTreeSource(@"C:\root", tree))
            .Scan(@"C:\root", new ScanTreeBudget(
                    maxRetainedNodes: 500,
                    maxChildrenPerDirectory: 4,
                    maxRetainedDepth: 6,
                    maxDegreeOfParallelism: 1,
                    parallelFanOutLevels: 0),
                CancellationToken.None, null);

        var actual = new BoundedDirectoryWalker(
                new SyntheticTreeSource(@"C:\root", tree),
                parallelizeTopLevel: true)
            .Scan(@"C:\root", budget, CancellationToken.None, null);

        Assert.Equal(expected.Total, actual.Total);
        Assert.Equal(expected.InaccessibleCount, actual.InaccessibleCount);
        AssertSameTree(expected.Root, actual.Root);
    }

    private static void AssertSameTree(FsItem expected, FsItem actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.HasUnretainedChildren, actual.HasUnretainedChildren);
        Assert.Equal(expected.Items is null, actual.Items is null);
        if (expected.Items is null)
            return;

        Assert.Equal(expected.Items.Count, actual.Items!.Count);
        for (var i = 0; i < expected.Items.Count; i++)
            AssertSameTree(expected.Items[i], actual.Items[i]);
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
