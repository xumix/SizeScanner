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

        var rootPath = target;
        if (rootPath[rootPath.Length - 1] != Path.DirectorySeparatorChar)
            rootPath += Path.DirectorySeparatorChar;

        ctx.ReportProgress(rootPath);
        long added = 0;
        var children = ctx.Scanner.Scan(rootPath, ref added);
        ctx.AddToTotal(added);
        if (children == null)
        {
            root.Items = null;
            ctx.AddProblematic(rootPath);
            return new ScanResult { Root = root, Total = ctx.Total, Inaccessible = ctx.SnapshotProblematic() };
        }

        root.AttachChildren(children);

        var subDirs = new List<FsItem>();
        foreach (var child in children)
            if (child.IsDir) subDirs.Add(child);

        if (subDirs.Count > 0 && !token.IsCancellationRequested)
        {
            if (_shouldParallelize(target))
            {
                var options = new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, subDirs.Count))
                };
                try
                {
                    Parallel.ForEach(subDirs, options,
                        child => WalkSequential(child, rootPath, ctx));
                }
                catch (OperationCanceledException)
                {
                    // Cancelled mid-scan; the partial tree is returned and the caller checks the token.
                }
            }
            else
            {
                foreach (var child in subDirs)
                {
                    if (token.IsCancellationRequested)
                        break;
                    WalkSequential(child, rootPath, ctx);
                }
            }
        }

        long size = 0;
        for (var i = 0; i < children.Count; i++)
            size += children[i].Size;
        root.Size = size;

        return new ScanResult { Root = root, Total = ctx.Total, Inaccessible = ctx.SnapshotProblematic() };
    }

    private static void WalkSequential(FsItem item, string? parentPath, WalkContext ctx)
    {
        if (ctx.Token.IsCancellationRequested) return;

        var scanObject = parentPath + item.Name;
        if (scanObject[scanObject.Length - 1] != Path.DirectorySeparatorChar)
            scanObject += Path.DirectorySeparatorChar;

        ctx.ReportProgress(scanObject);
        long added = 0;
        var children = ctx.Scanner.Scan(scanObject, ref added);
        ctx.AddToTotal(added);
        if (children == null)
        {
            item.Items = null;
            ctx.AddProblematic(scanObject);
            return;
        }

        item.AttachChildren(children);
        long size = 0;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.IsDir)
                WalkSequential(child, scanObject, ctx);
            size += child.Size;
        }
        item.Size = size;
    }

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
