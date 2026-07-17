// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;

namespace ScannerCore;

/// <summary>
/// Non-admin scan engine: enumerates directories via <see cref="DirectoryScanner"/>
/// (NtQueryDirectoryFile) and delegates the actual walk to the bounded, budget-aware
/// <see cref="BoundedDirectoryWalker"/>. Always available. Top-level subtrees walk in
/// parallel on SSD-class volumes only; spinning disks stay sequential.
/// </summary>
public sealed class DirectoryWalkEngine : IScanEngine
{
    private readonly Func<string, bool> _shouldParallelize;

    public DirectoryWalkEngine() : this(VolumeParallelismPolicy.ShouldParallelize) { }

    internal DirectoryWalkEngine(Func<string, bool> shouldParallelize) =>
        _shouldParallelize = shouldParallelize;

    public bool CanHandle(string target, bool isDriveScan, bool isElevated) => true;

    public ScanResult Scan(string target, bool isDriveScan, CancellationToken token, Action<string, long>? onProgress, ScanTreeBudget budget)
    {
        var source = new DirectoryScanner(preferAllocatedSize: isDriveScan);
        return new BoundedDirectoryWalker(source, parallelizeTopLevel: _shouldParallelize(target))
            .Scan(target, isDriveScan, budget, token, onProgress);
    }
}
