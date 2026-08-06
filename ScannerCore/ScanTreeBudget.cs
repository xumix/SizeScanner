// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;

namespace ScannerCore;

public sealed record ScanTreeBudget
{
    public static ScanTreeBudget Default { get; } = new();

    public ScanTreeBudget(
        int maxRetainedNodes = 100_000,
        int maxChildrenPerDirectory = 99,
        int maxRetainedDepth = 6,
        int maxInaccessiblePaths = 10_000,
        int maxDegreeOfParallelism = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedNodes, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChildrenPerDirectory, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedDepth, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInaccessiblePaths);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);

        MaxRetainedNodes = maxRetainedNodes;
        MaxChildrenPerDirectory = maxChildrenPerDirectory;
        MaxRetainedDepth = maxRetainedDepth;
        MaxInaccessiblePaths = maxInaccessiblePaths;
        MaxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    public int MaxRetainedNodes { get; }
    public int MaxChildrenPerDirectory { get; }
    public int MaxRetainedDepth { get; }
    public int MaxInaccessiblePaths { get; }
    public int MaxDegreeOfParallelism { get; }
}
