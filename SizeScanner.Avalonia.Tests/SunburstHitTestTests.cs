// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Avalonia;
using ScannerCore;
using SizeScanner.Avalonia.Charting;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class SunburstHitTestTests
{
    [Fact]
    public void HitTestableSegmentsByRing_groups_and_sorts_by_start_angle()
    {
        var root = TestTree.Dir("root",
            TestTree.File("a", 50),
            TestTree.File("b", 30),
            TestTree.File("c", 20));
        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        var ring0 = chart.HitTestableSegmentsByRing(0);

        Assert.NotEmpty(ring0);
        Assert.All(ring0, s => Assert.Equal(0, s.RingIndex));
        var angles = ring0.Select(s => s.StartAngle).ToArray();
        Assert.Equal(angles.OrderBy(x => x).ToArray(), angles);
    }

    [Fact]
    public void HitTest_resolves_inner_and_outer_ring_points()
    {
        var root = TestTree.Dir("root",
            TestTree.Dir("parent",
                TestTree.Dir("childDir", TestTree.File("g", 100)),
                TestTree.File("pf", 20)),
            TestTree.File("zzz", 10));
        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);
        Assert.Equal(2, chart.RingCount);

        // Center (100,100); straight up = angle 0, the first segment on each ring.
        var inner = SunburstHitTest.HitTest(chart, new Point(100, 60), new Size(200, 200));
        Assert.Equal("parent", inner?.Node?.Name);

        var outer = SunburstHitTest.HitTest(chart, new Point(100, 20), new Size(200, 200));
        Assert.Equal("childDir", outer?.Node?.Name);
    }

    [Fact]
    public void HitTest_resolves_synthetic_bands_so_they_stay_hoverable()
    {
        // The aggregate occupies the second half of ring 0, so a point to the lower-left
        // lands on the [Other] band it renders as.
        var root = TestTree.Dir("root",
            TestTree.File("kept", 100),
            FsItem.CreateAggregate(100));
        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        var hit = SunburstHitTest.HitTest(chart, new Point(100, 140), new Size(200, 200));

        Assert.NotNull(hit);
        Assert.Equal(ChartDisplayMetadata.OtherName, hit!.DisplayName);
    }

    [Fact]
    public void HitTest_returns_null_outside_the_rings()
    {
        var root = TestTree.Dir("root", TestTree.File("a", 100));
        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        // Center of the hole -> inside InnerHoleRatio, no ring.
        Assert.Null(SunburstHitTest.HitTest(chart, new Point(100, 100), new Size(200, 200)));
    }
}
