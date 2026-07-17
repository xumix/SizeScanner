// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ScannerCore;

/// <summary>
/// One-pass bounded post-order walker: computes exact totals while retaining only a
/// depth/width/count-bounded subset of the tree per <see cref="ScanTreeBudget"/>.
/// </summary>
internal sealed class BoundedDirectoryWalker(IDirectoryEntrySource source)
{
    private readonly IDirectoryEntrySource _source = source;

    internal ScanResult Scan(
        string target,
        bool isDriveScan,
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
            var root = WalkDirectory(
                target, target, 0, budget.MaxRetainedNodes,
                buffer, context);
            return new ScanResult
            {
                Root = root,
                Total = context.Total,
                Inaccessible = context.Inaccessible,
                InaccessibleCount = context.InaccessibleCount,
                InaccessiblePathsTruncated =
                    context.InaccessiblePathsTruncated
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

        var partition = depth + 1 >= context.Budget.MaxRetainedDepth
            ? new ChildAllowance(0, 0)
            : DivideAllowance(
                allowance, context.Budget.MaxChildrenPerDirectory);
        var collector = new BoundedChildCollector(partition.Slots);
        long total = 0;

        while (true)
        {
            context.Token.ThrowIfCancellationRequested();
            var sink = new BatchSink(collector);
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

        var selected = collector.BuildChildren();
        item.HasUnretainedChildren = collector.HasHiddenChildren;
        if (allowance <= 1 && item.HasUnretainedChildren)
            selected.Clear();
        item.AttachChildren(selected);
        item.Size = total;
        return item;
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

    private sealed class BatchSink(
        BoundedChildCollector collector) : IDirectoryEntrySink
    {
        public List<string> Directories { get; } = [];
        public long FileSize { get; private set; }

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
        private long _total;

        public IDirectoryEntrySource Source { get; } = source;
        public ScanTreeBudget Budget { get; } = budget;
        public CancellationToken Token { get; } = token;
        public long Total => _total;
        public IReadOnlyList<string> Inaccessible => _inaccessible;
        public long InaccessibleCount { get; private set; }
        public bool InaccessiblePathsTruncated =>
            InaccessibleCount > _inaccessible.Count;

        public void AddToTotal(long size) =>
            _total = checked(_total + size);

        public void AddInaccessible(string path)
        {
            InaccessibleCount = checked(InaccessibleCount + 1);
            if (_inaccessible.Count < Budget.MaxInaccessiblePaths)
                _inaccessible.Add(path);
        }

        public void Report(string path) =>
            onProgress?.Invoke(path, _total);
    }
}
