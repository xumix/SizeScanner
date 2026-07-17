// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class BoundedChildCollectorTests
{
    [Fact]
    public void Collector_keeps_largest_children_and_sums_the_rest()
    {
        var collector = new BoundedChildCollector(maxChildren: 2);
        collector.ConsiderDirectory(new FsItem("small", 10, true));
        collector.ConsiderDirectory(new FsItem("large", 30, true));
        collector.ConsiderDirectory(new FsItem("medium", 20, true));

        var children = collector.BuildChildren();

        Assert.Equal(["large", "medium"], children.Take(2).Select(x => x.Name));
        Assert.True(children[^1].IsAggregate);
        Assert.Equal(10, children[^1].Size);
        Assert.True(collector.HasHiddenChildren);
    }

    [Fact]
    public void Collector_uses_ordinal_name_as_stable_size_tie_breaker()
    {
        var collector = new BoundedChildCollector(maxChildren: 1);
        collector.ConsiderDirectory(new FsItem("z", 10, true));
        collector.ConsiderDirectory(new FsItem("a", 10, true));

        Assert.Equal("a", collector.BuildChildren()[0].Name);
    }
}
