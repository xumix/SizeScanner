// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using ScannerCore;
using SizeScanner.Avalonia.Charting;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class ChartHoverToolTipTests
{
    [Fact]
    public void BuildText_formats_root_first_with_tree_indent_and_sizes()
    {
        var kernel = TestTree.File("kernel.sys", 300);
        var windows = TestTree.Dir("Windows", kernel);
        var text = ChartHoverToolTip.BuildText([windows, kernel]);

        var lines = text.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Windows", lines[0]);
        Assert.Contains(Humanize.FsItem(windows), lines[0]);
        Assert.StartsWith("` kernel.sys", lines[1]);
        Assert.Contains(Humanize.FsItem(kernel), lines[1]);
    }

    [Fact]
    public void BuildText_returns_empty_for_no_items()
    {
        Assert.Equal(string.Empty, ChartHoverToolTip.BuildText([]));
    }
}
