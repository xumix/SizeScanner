// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ScannerCore;

/// <summary>
/// One-pass bounded post-order walker: computes exact totals while retaining only a
/// depth/width/count-bounded subset of the tree per <see cref="ScanTreeBudget"/>. When
/// <paramref name="parallelizeTopLevel"/> is set, the root's immediate subdirectories fan
/// out across a bounded worker pool sized by <see cref="ScanTreeBudget.MaxDegreeOfParallelism"/>;
/// every other level always walks sequentially.
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
        var context = new WalkContext(
            _source, budget, token, onProgress);
        var buffer = ArrayPool<byte>.Shared.Rent(
            DirectoryScanner.BufferSize);
        try
        {
            var parallelizeChildren =
                _parallelizeTopLevel && budget.MaxDegreeOfParallelism > 1;
            var root = WalkDirectory(
                target, target, 0, budget.MaxRetainedNodes,
                buffer, context, parallelizeChildren);
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
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private FsItem WalkDirectory(
        string path,
        string name,
        int depth,
        int allowance,
        byte[] buffer,
        WalkContext context,
        bool parallelizeChildren = false)
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

        var partition = depth + 1 >= context.Budget.MaxRetainedDepth
            ? new ChildAllowance(0, 0)
            : DivideAllowance(
                allowance, context.Budget.MaxChildrenPerDirectory);
        var collector = new BoundedChildCollector(partition.Slots);

        var total = parallelizeChildren
            ? WalkChildrenInParallel(
                cursor, path, depth, buffer, partition, collector, context)
            : WalkChildrenSequentially(
                cursor, path, depth, buffer, partition, collector, context);

        item.HasUnretainedChildren = collector.HasHiddenChildren;
        // An allowance of one leaves no room even for an aggregate child, so the
        // directory retains nothing; HasUnretainedChildren still keeps it scopable.
        item.AttachChildren(allowance <= 1 ? [] : collector.BuildChildren());
        item.Size = total;
        return item;
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
                var child = WalkDirectory(
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
    /// Enumerates <paramref name="path"/>'s own entries on the calling thread (files feed
    /// the local <paramref name="collector"/> directly), then fans its immediate
    /// subdirectories out across a bounded worker pool. A single consumer folds each
    /// completed subtree back into <paramref name="collector"/>, so completion order can
    /// never change output ordering versus the sequential walk.
    /// </summary>
    private long WalkChildrenInParallel(
        IDirectoryEntryCursor cursor,
        string path,
        int depth,
        byte[] buffer,
        ChildAllowance partition,
        BoundedChildCollector collector,
        WalkContext context)
    {
        long fileTotal = 0;
        var directoryNames = new List<string>();
        var sink = new BatchSink(collector);
        while (true)
        {
            context.Token.ThrowIfCancellationRequested();
            sink.Reset();
            var status = cursor.ReadNext(buffer, sink);
            fileTotal = checked(fileTotal + sink.FileSize);
            context.AddToTotal(sink.FileSize);
            directoryNames.AddRange(sink.Directories);

            if (status == DirectoryBatchResult.Completed)
                break;
            if (status == DirectoryBatchResult.Failed)
                throw new IOException(
                    $"Native directory enumeration failed for '{path}'.");
        }

        if (directoryNames.Count == 0)
            return fileTotal;

        var childTotal = RunChildWorkersAsync(
                path, depth + 1, directoryNames,
                partition.NodesPerChild, collector, context)
            .GetAwaiter().GetResult();
        return checked(fileTotal + childTotal);
    }

    private async Task<long> RunChildWorkersAsync(
        string parentPath,
        int childDepth,
        List<string> directoryNames,
        int allowance,
        BoundedChildCollector collector,
        WalkContext context)
    {
        var degree = Math.Max(1, context.Budget.MaxDegreeOfParallelism);
        var work = Channel.CreateBounded<DirectoryWorkItem>(
            new BoundedChannelOptions(2 * degree)
            {
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
        var results = Channel.CreateBounded<FsItem>(
            new BoundedChannelOptions(2 * degree)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var name in directoryNames)
                    await work.Writer.WriteAsync(
                        new DirectoryWorkItem(name), context.Token)
                        .ConfigureAwait(false);
            }
            finally
            {
                work.Writer.Complete();
            }
        });

        var workers = new Task[degree];
        for (var i = 0; i < degree; i++)
            workers[i] = RunWorkerAsync(
                parentPath, childDepth, allowance,
                work.Reader, results.Writer, context);

        // Supervises the producer/workers and always completes the results channel
        // (faulted or not) so the fan-in loop below never blocks forever.
        var fanOut = Task.Run(async () =>
        {
            Exception? failure = null;
            try { await producer.ConfigureAwait(false); }
            catch (Exception ex) { failure = ex; }
            try { await Task.WhenAll(workers).ConfigureAwait(false); }
            catch (Exception ex) { failure ??= ex; }
            results.Writer.Complete(failure);
        });

        long total = 0;
        try
        {
            while (await results.Reader.WaitToReadAsync(context.Token)
                       .ConfigureAwait(false))
            {
                while (results.Reader.TryRead(out var child))
                {
                    total = checked(total + child.Size);
                    collector.ConsiderDirectory(child);
                }
            }
        }
        finally
        {
            await fanOut.ConfigureAwait(false);
        }

        return total;
    }

    private async Task RunWorkerAsync(
        string parentPath,
        int childDepth,
        int allowance,
        ChannelReader<DirectoryWorkItem> reader,
        ChannelWriter<FsItem> resultsWriter,
        WalkContext context)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DirectoryScanner.BufferSize);
        try
        {
            while (await reader.WaitToReadAsync(context.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var work))
                {
                    var child = WalkDirectory(
                        Path.Combine(parentPath, work.Name),
                        work.Name,
                        childDepth,
                        allowance,
                        buffer,
                        context);
                    await resultsWriter.WriteAsync(child, context.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

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

    private readonly record struct DirectoryWorkItem(string Name);

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

    private sealed class WalkContext(
        IDirectoryEntrySource source,
        ScanTreeBudget budget,
        CancellationToken token,
        Action<string, long>? onProgress)
    {
        private readonly List<string> _inaccessible = [];
        private readonly object _inaccessibleLock = new();
        private long _total;
        private long _inaccessibleCount;

        public IDirectoryEntrySource Source { get; } = source;
        public ScanTreeBudget Budget { get; } = budget;
        public CancellationToken Token { get; } = token;
        public long Total => Interlocked.Read(ref _total);
        public long InaccessibleCount => Interlocked.Read(ref _inaccessibleCount);

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
            onProgress?.Invoke(path, Total);
    }
}
