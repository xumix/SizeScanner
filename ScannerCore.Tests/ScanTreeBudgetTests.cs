// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class ScanTreeBudgetTests
{
    [Fact]
    public void Budget_rejects_values_that_cannot_hold_root_and_aggregate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScanTreeBudget(maxRetainedNodes: 1));
    }

    [Fact]
    public void Budget_rejects_negative_fan_out_levels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScanTreeBudget(parallelFanOutLevels: -1));
    }

    [Fact]
    public void Budget_defaults_to_two_fan_out_levels()
    {
        Assert.Equal(2, new ScanTreeBudget().ParallelFanOutLevels);
    }

    [Fact]
    public void Zero_degree_resolves_to_bounded_processor_count()
    {
        var budget = new ScanTreeBudget(maxDegreeOfParallelism: 0);

        Assert.Equal(
            Math.Min(Environment.ProcessorCount, 16),
            budget.MaxDegreeOfParallelism);
    }

    [Fact]
    public void Explicit_degree_is_preserved()
    {
        Assert.Equal(3, new ScanTreeBudget(maxDegreeOfParallelism: 3).MaxDegreeOfParallelism);
    }
}
