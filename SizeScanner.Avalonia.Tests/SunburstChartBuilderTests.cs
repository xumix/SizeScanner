// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Avalonia;
using SizeScanner.Avalonia.Charting;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class SunburstChartBuilderTests
{
    [Fact]
    public void Empty_root_produces_empty_layout()
    {
        var root = TestTree.Dir("root");
        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.Empty(chart.Segments);
        Assert.Equal(0, chart.RingCount);
        Assert.Equal(0, chart.TotalSize);
    }

    [Fact]
    public void Level_zero_segments_are_on_inner_ring_and_split_full_circle()
    {
        var root = TestTree.Dir("root",
            TestTree.Dir("a",
                TestTree.File("a1", 60),
                TestTree.File("a2", 40)),     // a = 100
            TestTree.File("b", 100));         // total = 200

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.Equal(2, chart.RingCount);
        var outer = chart.Segments.Where(s => s.RingIndex == 0).ToArray();
        Assert.Equal(2, outer.Length);
        Assert.All(outer, s => Assert.Equal(0, s.Level));
        Assert.Equal(360d, outer.Sum(s => s.SweepAngle), 3);
        Assert.Contains(chart.Segments, s => s.Node?.Name == "a1" && s.RingIndex == 1);
    }

    [Fact]
    public void Items_at_or_below_threshold_become_non_actionable_placeholder_segments()
    {
        var root = TestTree.Dir("root",
            TestTree.File("big", 1000),
            TestTree.File("tiny", 5));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 10);

        Assert.Contains(chart.Segments, s => s.Node?.Name == "big" && !s.IsPlaceholder);
        var placeholder = Assert.Single(chart.Segments.Where(s => s.IsPlaceholder));
        Assert.Null(placeholder.Node);
        Assert.Equal(5, placeholder.Size);
    }

    [Fact]
    public void HitTest_returns_segment_matching_ring_and_angle()
    {
        var root = TestTree.Dir("root",
            TestTree.File("first", 100),
            TestTree.File("second", 100));
        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        // With 0 degrees at 12 o'clock and clockwise angles, the first half includes the right side.
        var hit = SunburstHitTest.HitTest(chart, new Point(80, 50), new Size(100, 100));

        Assert.NotNull(hit);
        Assert.Equal("first", hit.Node?.Name);
    }
}
