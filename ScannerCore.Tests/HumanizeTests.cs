// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class HumanizeTests
{
    [Theory]
    [InlineData(0, "0.00 Byte(s)")]
    [InlineData(512, "512.00 Byte(s)")]
    [InlineData(1024, "1024.00 Byte(s)")]
    [InlineData(1536, "1.50 KByte(s)")]
    [InlineData(1048576, "1024.00 KByte(s)")]
    public void Size_formats_expected_suffixes(long bytes, string expected)
    {
        Assert.Equal(expected, Humanize.Size(bytes));
    }

    [Fact]
    public void FsItem_denied_directory_returns_access_denied()
    {
        var item = new FsItem("secret", 0, isDir: true) { Items = null };
        Assert.Equal("<Access Denied>", Humanize.FsItem(item));
    }

    [Fact]
    public void FsItem_zero_size_returns_empty()
    {
        var item = new FsItem("empty.txt", 0, isDir: false);
        Assert.Equal("<Empty>", Humanize.FsItem(item));
    }

    [Fact]
    public void FsItem_nonzero_returns_size_string()
    {
        var item = new FsItem("a.bin", 1024, isDir: false);
        Assert.Equal("1024.00 Byte(s)", Humanize.FsItem(item));
    }
}
