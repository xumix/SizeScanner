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
}
