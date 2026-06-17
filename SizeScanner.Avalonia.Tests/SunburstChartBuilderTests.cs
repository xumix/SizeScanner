// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Avalonia;
using Avalonia.Media;
using ScannerCore;
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
    public void Segments_on_same_ring_are_emitted_largest_first()
    {
        var root = TestTree.Dir("root",
            TestTree.File("small", 10),
            TestTree.File("big", 90),
            TestTree.File("medium", 50));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        var ringZeroNames = chart.Segments
            .Where(s => s.RingIndex == 0)
            .Select(s => s.Node!.Name)
            .ToArray();
        Assert.Equal(new[] { "big", "medium", "small" }, ringZeroNames);
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

        Assert.Equal(1, chart.RingCount);
        var inner = chart.Segments.Where(s => s.RingIndex == 0).ToArray();
        Assert.Equal(2, inner.Length);
        Assert.All(inner, s => Assert.Equal(0, s.Level));
        Assert.Equal(360d, inner.Sum(s => s.SweepAngle), 3);
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "a1");
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "a2");
    }

    [Fact]
    public void Outermost_ring_is_omitted_when_it_would_only_contain_files()
    {
        var root = TestTree.Dir("root",
            TestTree.Dir("parent",
                TestTree.File("leaf", 50)),
            TestTree.Dir("sibling",
                TestTree.Dir("nested",
                    TestTree.File("inside", 25))));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.Equal(2, chart.RingCount);
        var outermost = chart.Segments.Where(s => s.RingIndex == chart.RingCount - 1).ToArray();
        Assert.All(outermost, s => Assert.True(s.Node!.IsDir));
        Assert.DoesNotContain(outermost, s => s.Node?.Name == "leaf");
        Assert.Contains(outermost, s => s.Node?.Name == "nested");
    }

    [Fact]
    public void Items_at_or_below_threshold_aggregate_into_root_ring_filtered_sector()
    {
        var root = TestTree.Dir("root",
            TestTree.File("big", 1000),
            TestTree.File("tiny", 5));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 10);

        Assert.Equal(1, chart.RingCount);

        var filtered = Assert.Single(chart.Segments, s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(0, filtered.RingIndex);
        Assert.Equal(5, filtered.Size);
        Assert.Equal(5d / 1005d * 360d, filtered.SweepAngle, 3);

        var big = Assert.Single(chart.Segments, s => s.Node?.Name == "big");
        Assert.Equal(0, big.RingIndex);
    }

    [Fact]
    public void Free_space_and_filtered_share_root_ring()
    {
        var root = TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 0),
            TestTree.File("big", 1000),
            TestTree.File("tiny", 5));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 10);

        Assert.Equal(1, chart.RingCount);
        Assert.All(
            chart.Segments.Where(s =>
                s.Node?.Name is DriveScanMetadata.FreeSpaceName
                    or DriveScanMetadata.InaccessibleName
                    or ChartDisplayMetadata.FilteredName
                    or "big"),
            segment => Assert.Equal(0, segment.RingIndex));
    }

    [Fact]
    public void Filtered_sizes_from_all_depths_aggregate_into_one_innermost_band()
    {
        const long total = 107;
        var root = TestTree.Dir("root",
            TestTree.Dir("parent",
                TestTree.File("visible-child", 100),
                TestTree.File("small-child", 4)),     // parent = 104 (> threshold)
            TestTree.File("small-root", 3));           // <= threshold

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 10);

        var parent = Assert.Single(chart.Segments, s => s.Node?.Name == "parent");
        Assert.Equal(0, parent.RingIndex);
        Assert.Equal(104, parent.Size);
        Assert.Equal(100d / total * 360d, parent.SweepAngle, 3);

        var filtered = Assert.Single(chart.Segments, s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(0, filtered.RingIndex);
        Assert.Equal(7, filtered.Size);
        Assert.Equal(7d / total * 360d, filtered.SweepAngle, 3);

        var ringZero = chart.Segments.Where(s => s.RingIndex == 0).ToArray();
        Assert.Equal(360d, ringZero.Sum(s => s.SweepAngle), 3);
    }

    [Fact]
    public void Directory_above_threshold_with_only_small_children_is_shown_not_filtered()
    {
        var root = TestTree.Dir("root",
            TestTree.Dir("bigDir",
                TestTree.File("c1", 8),
                TestTree.File("c2", 8),
                TestTree.File("c3", 8)),     // bigDir = 24 (> threshold), each child <= threshold
            TestTree.File("small-root", 5)); // <= threshold

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 10);

        var bigDir = Assert.Single(chart.Segments, s => s.Node?.Name == "bigDir");
        Assert.Equal(0, bigDir.RingIndex);
        Assert.Equal(24, bigDir.Size);

        var filtered = Assert.Single(chart.Segments, s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(5, filtered.Size);

        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "c1");
    }

    [Fact]
    public void Nested_items_below_threshold_inside_visible_directory_are_in_filtered_band()
    {
        var root = TestTree.Dir("root",
            TestTree.Dir("A",
                TestTree.Dir("big-sub",
                    TestTree.File("bs1", 50)),     // makes A recurse to ring 1
                TestTree.File("small-file", 3)));  // deeper filtered item

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 10);

        var filtered = Assert.Single(chart.Segments, s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(3, filtered.Size);
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "small-file");

        var a = Assert.Single(chart.Segments, s => s.Node?.Name == "A");
        Assert.Equal(53, a.Size);
        Assert.Equal(50d / 53d * 360d, a.SweepAngle, 3);
        Assert.Contains(chart.Segments, s => s.Node?.Name == "big-sub");
    }

    [Fact]
    public void Above_threshold_files_collapsed_from_outer_ring_are_not_in_filtered_band()
    {
        var root = TestTree.Dir("root",
            TestTree.Dir("parent",
                TestTree.File("leaf", 50)),
            TestTree.Dir("sibling",
                TestTree.Dir("nested",
                    TestTree.File("inside", 25))));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "leaf");
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "inside");
        Assert.Contains(chart.Segments, s => s.Node?.Name == "parent");
        Assert.Contains(chart.Segments, s => s.Node?.Name == "nested");
    }

    [Fact]
    public void Synthetic_segments_use_fixed_colors()
    {
        var root = TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 50),
            TestTree.File("big", 1000),
            TestTree.File("tiny", 5));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 10);

        var freeSpace = Assert.Single(chart.Segments, s => s.Node?.Name == DriveScanMetadata.FreeSpaceName);
        Assert.Equal(Colors.White, freeSpace.Color);

        var inaccessible = Assert.Single(chart.Segments, s => s.Node?.Name == DriveScanMetadata.InaccessibleName);
        Assert.Equal(Colors.Red, inaccessible.Color);

        var filtered = Assert.Single(chart.Segments, s => s.Node?.Name == ChartDisplayMetadata.FilteredName);
        Assert.Equal(Color.FromRgb(128, 128, 128), filtered.Color);
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
