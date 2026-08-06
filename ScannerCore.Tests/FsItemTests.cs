// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class FsItemTests
{
    [Fact]
    public void Constructor_sets_core_fields()
    {
        var item = new FsItem("name.txt", 123, isDir: false);
        Assert.Equal("name.txt", item.Name);
        Assert.Equal(123, item.Size);
        Assert.False(item.IsDir);
        Assert.Null(item.Parent);
        Assert.Null(item.Items);
    }

    [Fact]
    public void FsItem_has_no_lastModified_member()
    {
        Assert.Null(typeof(FsItem).GetProperty("LastModified"));
    }

    [Fact]
    public void FsItem_is_sealed()
    {
        Assert.True(typeof(FsItem).IsSealed);
    }

    [Fact]
    public void Aggregate_is_non_directory_non_actionable_core_node()
    {
        var aggregate = FsItem.CreateAggregate(1_024);

        Assert.Equal(FsItemKind.Aggregate, aggregate.Kind);
        Assert.Equal(string.Empty, aggregate.Name);
        Assert.Equal(1_024, aggregate.Size);
        Assert.False(aggregate.IsDir);
        Assert.Null(aggregate.Items);
    }

    [Fact]
    public void CountRetainedNodes_counts_only_the_bounded_object_graph()
    {
        var root = new FsItem("root", 10, isDir: true);
        root.AttachChildren(
        [
            new FsItem("kept.bin", 5, isDir: false),
            FsItem.CreateAggregate(5)
        ]);

        Assert.Equal(3, root.CountRetainedNodes());
    }
}
