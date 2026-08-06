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
}
