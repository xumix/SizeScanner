// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using ScannerCore;
using SizeScanner.Avalonia.Charting;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class SunburstChartBuilderCapTests
{
    [Fact]
    public void Build_caps_total_segment_count_for_huge_flat_tree()
    {
        var files = new FsItem[50_000];
        for (var i = 0; i < files.Length; i++)
            files[i] = TestTree.File($"f{i}", i + 1);
        var root = TestTree.Dir("root", files);

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.True(chart.Segments.Count <= SunburstChartBuilder.MaxSegments,
            $"segments={chart.Segments.Count}");
    }

    [Fact]
    public void Build_does_not_cap_small_trees()
    {
        var root = TestTree.Dir("root",
            TestTree.File("a", 50),
            TestTree.File("b", 50));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.Equal(2, chart.Segments.Count);
    }
}
