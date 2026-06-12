// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Tree_helper_sums_child_sizes()
    {
        var root = TestTree.Dir("root",
            TestTree.File("a", 100),
            TestTree.File("b", 50));

        Assert.Equal(150, root.Size);
        Assert.Equal(2, root.Items!.Count);
        Assert.Same(root, root.Items![0].Parent);
    }
}
