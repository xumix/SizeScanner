// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ScannerCore;

/// <summary>
/// Non-admin scan engine: enumerates directories via <see cref="DirectoryScanner"/>
/// (NtQueryDirectoryFile). Always available. Top-level subtrees walk in parallel on
/// SSD-class volumes only; spinning disks stay sequential.
/// </summary>
public sealed class DirectoryWalkEngine : IScanEngine
{
    private readonly Func<string, bool> _shouldParallelize;

    public DirectoryWalkEngine() : this(VolumeParallelismPolicy.ShouldParallelize) { }

    internal DirectoryWalkEngine(Func<string, bool> shouldParallelize) =>
        _shouldParallelize = shouldParallelize;
    public bool CanHandle(string target, bool isDriveScan, bool isElevated) => true;

    public ScanResult Scan(string target, bool isDriveScan, CancellationToken token, Action<string, long>? onProgress)
    {
        var ctx = new WalkContext(isDriveScan, token, onProgress);
        var root = new FsItem(target, 0, isDir: true);
        var rootPath = EnsureTrailingSeparator(target);
        WalkDirectory(root, rootPath, ctx, parallelChildren: _shouldParallelize(target));

        return new ScanResult { Root = root, Total = ctx.Total, Inaccessible = ctx.SnapshotProblematic() };
    }

    private static void WalkDirectory(FsItem item, string path, WalkContext ctx, bool parallelChildren)
    {
        if (ctx.Token.IsCancellationRequested) return;

        var scanPath = EnsureTrailingSeparator(path);
        if (!TryScanChildren(item, scanPath, ctx, out var children))
            return;

        if (children.Count > 0 && !ctx.Token.IsCancellationRequested)
        {
            if (parallelChildren)
                WalkChildDirectoriesInParallel(children, scanPath, ctx);
            else
                WalkChildDirectoriesSequentially(children, scanPath, ctx);
        }

        item.Size = SumChildSizes(children);
    }

    private static void WalkChildDirectoriesInParallel(IReadOnlyList<FsItem> children, string parentPath, WalkContext ctx)
    {
        var subDirs = GetSubDirectories(children);
        if (subDirs.Count == 0)
            return;

        var options = new ParallelOptions
        {
            CancellationToken = ctx.Token,
            MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, subDirs.Count))
        };

        try
        {
            Parallel.ForEach(subDirs, options,
                child => WalkDirectory(child, Path.Combine(parentPath, child.Name), ctx, parallelChildren: false));
        }
        catch (OperationCanceledException)
        {
            // Cancelled mid-scan; the partial tree is returned and the caller checks the token.
        }
    }

    private static void WalkChildDirectoriesSequentially(IReadOnlyList<FsItem> children, string parentPath, WalkContext ctx)
    {
        foreach (var child in children)
        {
            if (ctx.Token.IsCancellationRequested)
                break;
            if (child.IsDir)
                WalkDirectory(child, Path.Combine(parentPath, child.Name), ctx, parallelChildren: false);
        }
    }

    private static List<FsItem> GetSubDirectories(IReadOnlyList<FsItem> children)
    {
        var subDirs = new List<FsItem>();
        foreach (var child in children)
            if (child.IsDir)
                subDirs.Add(child);
        return subDirs;
    }

    private static bool TryScanChildren(FsItem item, string path, WalkContext ctx, out List<FsItem> children)
    {
        ctx.ReportProgress(path);
        long added = 0;
        var scanned = ctx.Scanner.Scan(path, ref added);
        ctx.AddToTotal(added);
        if (scanned is null)
        {
            item.Items = null;
            ctx.AddProblematic(path);
            children = [];
            return false;
        }

        item.AttachChildren(scanned);
        children = scanned;
        return true;
    }

    private static long SumChildSizes(IReadOnlyList<FsItem> children)
    {
        long size = 0;
        for (var i = 0; i < children.Count; i++)
            size += children[i].Size;
        return size;
    }

    private static string EnsureTrailingSeparator(string path) =>
        path[path.Length - 1] == Path.DirectorySeparatorChar
            ? path
            : path + Path.DirectorySeparatorChar;

    private sealed class WalkContext
    {
        private readonly Action<string, long>? _onProgress;
        private readonly List<string> _problematic = new();
        private readonly object _problematicLock = new();
        private long _total;

        public WalkContext(bool useAllocationSize, CancellationToken token, Action<string, long>? onProgress)
        {
            Scanner = new DirectoryScanner(useAllocationSize);
            Token = token;
            _onProgress = onProgress;
        }

        public DirectoryScanner Scanner { get; }
        public CancellationToken Token { get; }
        public long Total => Interlocked.Read(ref _total);

        public void AddToTotal(long bytes) => Interlocked.Add(ref _total, bytes);

        public void AddProblematic(string path)
        {
            lock (_problematicLock) _problematic.Add(path);
        }

        public IReadOnlyList<string> SnapshotProblematic()
        {
            lock (_problematicLock) return _problematic.ToArray();
        }

        public void ReportProgress(string path) => _onProgress?.Invoke(path, Total);
    }
}
