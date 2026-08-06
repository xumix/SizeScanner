// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;

namespace ScannerCore;

public sealed record ScanTreeBudget
{
    public static ScanTreeBudget Default { get; } = new();

    /// <param name="maxDegreeOfParallelism">
    /// Shared slots for concurrent native reads and sequential subtree walks.
    /// Zero resolves to <c>Math.Min(Environment.ProcessorCount, 16)</c>.
    /// </param>
    /// <param name="parallelFanOutLevels">
    /// Number of tree levels that fan their children out: 0 sequential,
    /// 1 root only, 2 root plus its immediate children.
    /// </param>
    public ScanTreeBudget(
        int maxRetainedNodes = 100_000,
        int maxChildrenPerDirectory = 99,
        int maxRetainedDepth = 6,
        int maxInaccessiblePaths = 10_000,
        int maxDegreeOfParallelism = 0,
        int parallelFanOutLevels = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedNodes, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChildrenPerDirectory, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedDepth, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInaccessiblePaths);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDegreeOfParallelism);
        ArgumentOutOfRangeException.ThrowIfNegative(parallelFanOutLevels);

        MaxRetainedNodes = maxRetainedNodes;
        MaxChildrenPerDirectory = maxChildrenPerDirectory;
        MaxRetainedDepth = maxRetainedDepth;
        MaxInaccessiblePaths = maxInaccessiblePaths;
        MaxDegreeOfParallelism = maxDegreeOfParallelism == 0
            ? Math.Min(Environment.ProcessorCount, 16)
            : maxDegreeOfParallelism;
        ParallelFanOutLevels = parallelFanOutLevels;
    }

    public int MaxRetainedNodes { get; }
    public int MaxChildrenPerDirectory { get; }
    public int MaxRetainedDepth { get; }
    public int MaxInaccessiblePaths { get; }
    public int MaxDegreeOfParallelism { get; }
    public int ParallelFanOutLevels { get; }
}
