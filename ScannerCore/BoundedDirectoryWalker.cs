// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ScannerCore;

/// <summary>
/// One-pass bounded post-order walker: computes exact totals while retaining only a
/// depth/width/count-bounded subset of the tree per <see cref="ScanTreeBudget"/>.
/// Directories above <see cref="ScanTreeBudget.ParallelFanOutLevels"/> fan their children
/// out; everything below walks its whole subtree synchronously on one slot of a scan-wide
/// semaphore. A slot is held only around a native read or a sequential subtree, never
/// across a wait for children, so no wait cycle can form.
/// </summary>
internal sealed class BoundedDirectoryWalker(
    IDirectoryEntrySource source,
    bool parallelizeTopLevel = false)
{
    private readonly IDirectoryEntrySource _source = source;
    private readonly bool _parallelizeTopLevel = parallelizeTopLevel;

    internal ScanResult Scan(
        string target,
        ScanTreeBudget budget,
        CancellationToken token,
        Action<string, long>? onProgress)
    {
        var degree = _parallelizeTopLevel
            ? Math.Max(1, budget.MaxDegreeOfParallelism)
            : 1;
        var levels = _parallelizeTopLevel ? budget.ParallelFanOutLevels : 0;

        var context = new WalkContext(
            _source, budget, levels, degree, token, onProgress);

        // The only blocking wait in the walk, on the caller's own thread.
        var root = WalkAsync(
                target, target, 0, budget.MaxRetainedNodes, context)
            .GetAwaiter().GetResult();

        var inaccessible = context.SnapshotInaccessible();
        var inaccessibleCount = context.InaccessibleCount;
        return new ScanResult
        {
            Root = root,
            Total = context.Total,
            Inaccessible = inaccessible,
            InaccessibleCount = inaccessibleCount,
            InaccessiblePathsTruncated =
                inaccessibleCount > inaccessible.Length
        };
    }

    private async Task<FsItem> WalkAsync(
        string path,
        string name,
        int depth,
        int allowance,
        WalkContext context)
    {
        if (depth < context.FanOutLevels)
            return await WalkFanOutAsync(path, name, depth, allowance, context)
                .ConfigureAwait(false);

        context.Token.ThrowIfCancellationRequested();
        await context.AcquireSlotAsync().ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(DirectoryScanner.BufferSize);
        try
        {
            return WalkSequential(path, name, depth, allowance, buffer, context);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            context.ReleaseSlot();
        }
    }

    private FsItem WalkSequential(
        string path,
        string name,
        int depth,
        int allowance,
        byte[] buffer,
        WalkContext context)
    {
        context.Token.ThrowIfCancellationRequested();
        context.Report(path);

        var item = new FsItem(name, 0, isDir: true);
        using var cursor = context.Source.Open(EnsureTrailingSeparator(path));
        if (cursor is null)
        {
            item.Items = null;
            context.AddInaccessible(path);
            return item;
        }

        var partition = DividePartition(depth, allowance, context);
        var collector = new BoundedChildCollector(partition.Slots);
        var total = WalkChildrenSequentially(
            cursor, path, depth, buffer, partition, collector, context);
        return Complete(item, collector, allowance, total);
    }

    private long WalkChildrenSequentially(
        IDirectoryEntryCursor cursor,
        string path,
        int depth,
        byte[] buffer,
        ChildAllowance partition,
        BoundedChildCollector collector,
        WalkContext context)
    {
        long total = 0;
        // One sink for the whole directory, reset per batch: recursing batch by batch
        // keeps only one batch of subdirectory names alive, which is what bounds memory
        // on directories with millions of entries.
        var sink = new BatchSink(collector);
        while (true)
        {
            context.Token.ThrowIfCancellationRequested();
            sink.Reset();
            var status = cursor.ReadNext(buffer, sink);
            total = checked(total + sink.FileSize);
            context.AddToTotal(sink.FileSize);

            foreach (var childName in sink.Directories)
            {
                var child = WalkSequential(
                    Path.Combine(path, childName),
                    childName,
                    depth + 1,
                    partition.NodesPerChild,
                    buffer,
                    context);
                total = checked(total + child.Size);
                collector.ConsiderDirectory(child);
            }

            if (status == DirectoryBatchResult.Completed)
                break;
            if (status == DirectoryBatchResult.Failed)
                throw new IOException(
                    $"Native directory enumeration failed for '{path}'.");
        }

        return total;
    }

    /// <summary>
    /// Reads <paramref name="path"/> one batch at a time under a slot and schedules each
    /// discovered subdirectory into a bounded in-flight window, draining completed children
    /// into the collector as it goes. The slot is released before any wait on a child, and
    /// the window keeps the number of live subdirectory names bounded regardless of how
    /// many entries the directory holds.
    /// </summary>
    private async Task<FsItem> WalkFanOutAsync(
        string path,
        string name,
        int depth,
        int allowance,
        WalkContext context)
    {
        context.Token.ThrowIfCancellationRequested();
        context.Report(path);

        var item = new FsItem(name, 0, isDir: true);
        using var cursor = context.Source.Open(EnsureTrailingSeparator(path));
        if (cursor is null)
        {
            item.Items = null;
            context.AddInaccessible(path);
            return item;
        }

        var partition = DividePartition(depth, allowance, context);
        var collector = new BoundedChildCollector(partition.Slots);
        var sink = new BatchSink(collector);
        var pending = new List<Task<FsItem>>(context.ChildWindow);

        long total = 0;
        var completed = false;
        while (!completed)
        {
            context.Token.ThrowIfCancellationRequested();
            completed = await ReadBatchAsync(cursor, path, sink, context)
                .ConfigureAwait(false);
            total = checked(total + sink.FileSize);

            foreach (var childName in sink.Directories)
            {
                while (pending.Count >= context.ChildWindow)
                    total = checked(total + await DrainOneAsync(pending, collector)
                        .ConfigureAwait(false));

                var childPath = Path.Combine(path, childName);
                pending.Add(Task.Run(
                    () => WalkAsync(
                        childPath,
                        childName,
                        depth + 1,
                        partition.NodesPerChild,
                        context),
                    context.Token));
            }
        }

        while (pending.Count > 0)
            total = checked(total + await DrainOneAsync(pending, collector)
                .ConfigureAwait(false));

        return Complete(item, collector, allowance, total);
    }

    /// <summary>
    /// Reads one native batch while holding a slot, and returns whether the directory is
    /// exhausted. The pooled buffer is rented and returned inside the slot, so outstanding
    /// rentals never exceed the configured degree.
    /// </summary>
    private static async Task<bool> ReadBatchAsync(
        IDirectoryEntryCursor cursor,
        string path,
        BatchSink sink,
        WalkContext context)
    {
        await context.AcquireSlotAsync().ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(DirectoryScanner.BufferSize);
        try
        {
            sink.Reset();
            var status = cursor.ReadNext(buffer, sink);
            context.AddToTotal(sink.FileSize);
            if (status == DirectoryBatchResult.Failed)
                throw new IOException(
                    $"Native directory enumeration failed for '{path}'.");
            return status == DirectoryBatchResult.Completed;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            context.ReleaseSlot();
        }
    }

    private static async Task<long> DrainOneAsync(
        List<Task<FsItem>> pending,
        BoundedChildCollector collector)
    {
        var finished = await Task.WhenAny(pending).ConfigureAwait(false);
        pending.Remove(finished);
        var child = await finished.ConfigureAwait(false);
        collector.ConsiderDirectory(child);
        return child.Size;
    }

    private static FsItem Complete(
        FsItem item,
        BoundedChildCollector collector,
        int allowance,
        long total)
    {
        item.HasUnretainedChildren = collector.HasHiddenChildren;
        // An allowance of one leaves no room even for an aggregate child, so the
        // directory retains nothing; HasUnretainedChildren still keeps it scopable.
        item.AttachChildren(allowance <= 1 ? [] : collector.BuildChildren());
        item.Size = total;
        return item;
    }

    private static ChildAllowance DividePartition(
        int depth,
        int allowance,
        WalkContext context) =>
        depth + 1 >= context.Budget.MaxRetainedDepth
            ? new ChildAllowance(0, 0)
            : DivideAllowance(allowance, context.Budget.MaxChildrenPerDirectory);

    private static ChildAllowance DivideAllowance(
        int allowance,
        int maxChildren)
    {
        if (allowance < 3)
            return new(0, 0);

        var slots = Math.Min(maxChildren, allowance - 3);
        var nodesPerChild = Math.Max(
            1, (allowance - 2) / (slots + 1));
        return new(slots, nodesPerChild);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private readonly record struct ChildAllowance(
        int Slots,
        int NodesPerChild);

    /// <summary>
    /// Collects one native batch: files go straight into the collector, subdirectory names
    /// are handed back to the walker. <see cref="Reset"/> before each batch so
    /// <see cref="FileSize"/> stays a per-batch delta.
    /// </summary>
    private sealed class BatchSink(
        BoundedChildCollector collector) : IDirectoryEntrySink
    {
        public List<string> Directories { get; } = [];
        public long FileSize { get; private set; }

        public void Reset()
        {
            Directories.Clear();
            FileSize = 0;
        }

        public void OnEntry(
            ReadOnlySpan<char> name,
            long size,
            bool isDirectory)
        {
            if (isDirectory)
            {
                Directories.Add(name.ToString());
                return;
            }

            FileSize = checked(FileSize + size);
            collector.ConsiderFile(name, size);
        }
    }

    private sealed class WalkContext
    {
        private readonly List<string> _inaccessible = [];
        private readonly object _inaccessibleLock = new();
        private readonly SemaphoreSlim _slots;
        private long _total;
        private long _inaccessibleCount;

        public WalkContext(
            IDirectoryEntrySource source,
            ScanTreeBudget budget,
            int fanOutLevels,
            int degree,
            CancellationToken token,
            Action<string, long>? onProgress)
        {
            Source = source;
            Budget = budget;
            FanOutLevels = fanOutLevels;
            ChildWindow = Math.Max(2, 2 * degree);
            Token = token;
            OnProgress = onProgress;
            _slots = new SemaphoreSlim(degree, degree);
        }

        public IDirectoryEntrySource Source { get; }
        public ScanTreeBudget Budget { get; }
        public int FanOutLevels { get; }
        public int ChildWindow { get; }
        public CancellationToken Token { get; }
        private Action<string, long>? OnProgress { get; }
        public long Total => Interlocked.Read(ref _total);
        public long InaccessibleCount => Interlocked.Read(ref _inaccessibleCount);

        // The semaphore is deliberately never disposed: it holds no wait handle (we never
        // touch AvailableWaitHandle), and disposing it could fault a straggling task.
        public Task AcquireSlotAsync() => _slots.WaitAsync(Token);

        public void ReleaseSlot() => _slots.Release();

        public void AddToTotal(long size) =>
            Interlocked.Add(ref _total, size);

        public string[] SnapshotInaccessible()
        {
            lock (_inaccessibleLock)
                return _inaccessible.ToArray();
        }

        public void AddInaccessible(string path)
        {
            Interlocked.Increment(ref _inaccessibleCount);
            lock (_inaccessibleLock)
            {
                if (_inaccessible.Count < Budget.MaxInaccessiblePaths)
                    _inaccessible.Add(path);
            }
        }

        public void Report(string path) =>
            OnProgress?.Invoke(path, Total);
    }
}
