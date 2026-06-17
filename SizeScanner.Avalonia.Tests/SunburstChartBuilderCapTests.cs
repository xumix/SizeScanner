// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
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
        Assert.Contains(chart.Segments, s => s.Node?.Name == "f49999");
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "f0");
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

    [Fact]
    public void Build_aggregates_children_beyond_per_sector_limit_into_other()
    {
        var files = new FsItem[105];
        for (var i = 0; i < files.Length; i++)
            files[i] = TestTree.File($"f{i}", i + 1);
        var root = TestTree.Dir("root", files);

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.Equal(100, chart.Segments.Count);
        Assert.Contains(chart.Segments, s => s.Node?.Name == "f104");
        Assert.Contains(chart.Segments, s => s.Node?.Name == "f6");
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "f5");
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "f0");

        var other = Assert.Single(chart.Segments, s => s.Node?.Name == ChartDisplayMetadata.OtherName);
        var expectedOtherSize = Enumerable.Range(1, 6).Sum(i => (long)i);
        var expectedTotal = Enumerable.Range(1, 105).Sum(i => (long)i);
        Assert.Equal(expectedOtherSize, other.Size);
        Assert.Equal(expectedOtherSize / (double)expectedTotal * 360d, other.SweepAngle, 3);
    }

    [Fact]
    public void Build_sector_budget_spans_all_ring_levels()
    {
        var subs = new FsItem[105];
        for (var i = 0; i < subs.Length; i++)
            subs[i] = TestTree.Dir($"sub{i}", TestTree.File($"f{i}", i + 1));
        var root = TestTree.Dir("root", TestTree.Dir("bigDir", subs));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.Equal(100, chart.Segments.Count);
        Assert.Contains(chart.Segments, s => s.Node?.Name == "bigDir");
        Assert.Contains(chart.Segments, s => s.Node?.Name == "sub104");
        Assert.Contains(chart.Segments, s => s.Node?.Name == "sub7");
        Assert.Contains(chart.Segments, s => s.Node?.Name == ChartDisplayMetadata.OtherName);
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "sub6");
        Assert.DoesNotContain(chart.Segments, s => s.Node?.Name == "sub0");
    }

    [Fact]
    public void Build_root_ring_completes_when_first_child_has_many_descendants()
    {
        var subs = new FsItem[105];
        for (var i = 0; i < subs.Length; i++)
            subs[i] = TestTree.Dir($"sub{i}", TestTree.File($"f{i}", i + 1));
        var root = TestTree.Dir("C:\\",
            TestTree.Dir("Users", subs),
            TestTree.Dir("Games", TestTree.File("game", 50)),
            TestTree.Dir("Windows", TestTree.File("win", 100)));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        var ringZero = chart.Segments.Where(s => s.RingIndex == 0).ToArray();
        Assert.Equal(360d, ringZero.Sum(s => s.SweepAngle), 3);
        Assert.Contains(ringZero, s => s.Node?.Name == "Users");
        Assert.Contains(ringZero, s => s.Node?.Name == "Games");
        Assert.Contains(ringZero, s => s.Node?.Name == "Windows");
    }

    [Fact]
    public void Build_deep_subtree_does_not_starve_large_siblings()
    {
        var system32Subs = new FsItem[105];
        for (var i = 0; i < system32Subs.Length; i++)
            system32Subs[i] = TestTree.Dir($"s{i}", TestTree.File($"f{i}", i + 1));

        var root = TestTree.Dir("C:\\",
            TestTree.Dir("Windows",
                TestTree.Dir("System32", system32Subs),
                TestTree.Dir("WinSxS", TestTree.File("w", 5000)),
                TestTree.Dir("Installer", TestTree.File("i", 4000)),
                TestTree.Dir("Assembly", TestTree.File("a", 3000))),
            TestTree.Dir("Program Files", TestTree.File("pf", 100)));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        var windowsSector = chart.Segments.Single(s => s.Node?.Name == "Windows" && s.RingIndex == 0);
        var windowsSegments = SegmentsInSector(chart, windowsSector);

        Assert.Contains(windowsSegments, s => s.Node?.Name == "System32");
        Assert.Contains(windowsSegments, s => s.Node?.Name == "WinSxS");
        Assert.Contains(windowsSegments, s => s.Node?.Name == "Installer");
        Assert.Contains(windowsSegments, s => s.Node?.Name == "Assembly");

        var ringOneNames = windowsSegments
            .Where(s => s.RingIndex == 1)
            .Select(s => s.Node?.Name)
            .ToArray();
        Assert.Contains("WinSxS", ringOneNames);
        Assert.Contains("Installer", ringOneNames);
        Assert.Contains("Assembly", ringOneNames);
    }

    [Fact]
    public void Build_caps_each_root_sector_independently_at_per_sector_limit()
    {
        var usersSubs = new FsItem[105];
        for (var i = 0; i < usersSubs.Length; i++)
            usersSubs[i] = TestTree.Dir($"u{i}", TestTree.File($"uf{i}", i + 1));
        var gamesSubs = new FsItem[105];
        for (var i = 0; i < gamesSubs.Length; i++)
            gamesSubs[i] = TestTree.Dir($"g{i}", TestTree.File($"gf{i}", i + 1));

        var root = TestTree.Dir("C:\\",
            TestTree.Dir("Users", usersSubs),
            TestTree.Dir("Games", gamesSubs));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        // Both root sectors are drawn and the root ring is complete.
        var ringZero = chart.Segments.Where(s => s.RingIndex == 0).ToArray();
        Assert.Equal(360d, ringZero.Sum(s => s.SweepAngle), 3);
        Assert.Contains(ringZero, s => s.Node?.Name == "Users");
        Assert.Contains(ringZero, s => s.Node?.Name == "Games");

        // Each root sector (its segment plus all descendants) stays at or below the per-sector limit.
        var usersSectorCount = CountSectorSegments(chart, "Users");
        var gamesSectorCount = CountSectorSegments(chart, "Games");
        Assert.True(usersSectorCount <= SunburstChartBuilder.MaxSegmentsPerSector, $"Users sector={usersSectorCount}");
        Assert.True(gamesSectorCount <= SunburstChartBuilder.MaxSegmentsPerSector, $"Games sector={gamesSectorCount}");
        Assert.Equal(SunburstChartBuilder.MaxSegmentsPerSector, usersSectorCount);
        Assert.Equal(SunburstChartBuilder.MaxSegmentsPerSector, gamesSectorCount);
    }

    [Fact]
    public void Build_ring_count_reflects_rings_actually_drawn_under_budget()
    {
        // 'big' would nest three levels deep (CountRings predicts ring index 2), but its 150
        // direct children fill ring 1 and exhaust the per-sector budget, so ring 2 is never drawn.
        var subs = new FsItem[150];
        for (var i = 0; i < subs.Length; i++)
            subs[i] = TestTree.Dir($"sub{i}", TestTree.Dir($"deep{i}", TestTree.File($"f{i}", i + 1)));
        var root = TestTree.Dir("root", TestTree.Dir("big", subs));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        var maxRingIndex = chart.Segments.Max(s => s.RingIndex);
        Assert.Equal(1, maxRingIndex);
        Assert.Equal(2, chart.RingCount);
        Assert.DoesNotContain(chart.Segments, s => s.RingIndex >= 2);
    }

    private static int CountSectorSegments(SunburstChart chart, string rootName)
    {
        var rootSegment = chart.Segments.Single(s => s.Node?.Name == rootName && s.RingIndex == 0);
        return SegmentsInSector(chart, rootSegment).Count;
    }

    private static List<SunburstSegment> SegmentsInSector(SunburstChart chart, SunburstSegment rootSegment)
    {
        var start = rootSegment.StartAngle;
        var end = start + rootSegment.SweepAngle;
        const double epsilon = 1e-6;

        var result = new List<SunburstSegment>();
        foreach (var segment in chart.Segments)
        {
            var mid = segment.StartAngle + (segment.SweepAngle / 2d);
            if (mid >= start - epsilon && mid < end - epsilon)
                result.Add(segment);
        }
        return result;
    }
}
