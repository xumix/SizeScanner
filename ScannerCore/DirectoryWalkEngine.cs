// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ScannerCore;

/// <summary>
/// Non-admin scan engine: enumerates directories via <see cref="DirectoryScanner"/>
/// (NtQueryDirectoryFile). Always available. Parallelism is added in a later task.
/// </summary>
public sealed class DirectoryWalkEngine : IScanEngine
{
    public bool CanHandle(string target, bool isDriveScan, bool isElevated) => true;

    public ScanResult Scan(string target, bool isDriveScan, CancellationToken token, Action<string, long>? onProgress)
    {
        var ctx = new WalkContext(isDriveScan, token, onProgress);
        var root = new FsItem(target, 0, isDir: true);
        WalkSequential(root, null, ctx);
        return new ScanResult
        {
            Root = root,
            Total = ctx.Total,
            Inaccessible = ctx.SnapshotProblematic()
        };
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
