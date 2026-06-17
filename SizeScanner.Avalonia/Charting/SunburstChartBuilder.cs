// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

/// <summary>
/// Builds a true sunburst layout: each segment has a ring, angular span, color, and optional FsItem.
/// An item is filtered only when its own aggregate size is at or below the threshold. Filtered items
/// at any depth are aggregated into a single innermost-ring sector; visible directories keep their
/// true size in the legend while their arc excludes filtered descendants. A directory whose total
/// exceeds the threshold is always shown, even when every child is individually filtered. The
/// outermost ring is created only when it would contain real directories; file-only leaves stay on
/// the inner ring.
/// </summary>
public sealed class SunburstChartBuilder
{
    public const int MaxSegments = 100_000;
    public const int MaxSegmentsPerSector = 100;

    private static readonly Color FreeSpaceColor = Colors.White;
    private static readonly Color FilteredBandColor = Color.FromRgb(128, 128, 128);
    private static readonly Color OtherBandColor = Color.FromRgb(96, 96, 96);
    private static readonly Color InaccessibleColor = Colors.Red;

    private long _filterThreshold;
    private long _filteredTotal;
    private readonly Dictionary<FsItem, long> _displayedSize = new();
    private readonly List<SunburstSegment> _segments = new();
    private int _ringCount;

    public SunburstChart Build(FsItem root, long filterThreshold)
    {
        _filterThreshold = filterThreshold;
        _filteredTotal = 0;
        _displayedSize.Clear();
        _segments.Clear();
        _ringCount = root.Items is { Count: > 0 } ? CountRings(root, level: 0) : 0;

        if (_ringCount == 0)
            return new SunburstChart([], 0, 0);

        var total = FilterThreshold.GetDisplayTotal(root);
        if (total <= 0)
            return new SunburstChart([], 0, 0);

        ComputeDisplayedSizes(root);
        var sectorBudget = MaxSegmentsPerSector;
        var endAngle = AddChildren(
            root.Items!,
            level: 0,
            startAngle: 0d,
            sweepAngle: 360d,
            parentColor: null,
            denominator: total,
            ref sectorBudget);
        if (_filteredTotal > 0)
            AddFilteredRootSector(endAngle);

        return new SunburstChart(_segments, _ringCount, total);
    }

    private int CountRings(FsItem node, int level)
    {
        if (node.Items is not { Count: > 0 })
            return level;

        var max = level + 1;
        foreach (var child in node.Items)
        {
            if (!IsVisibleChild(child))
                continue;
            if (!IsRealDirectory(child) || child.Items is not { Count: > 0 })
                continue;
            if (AnyVisibleRealDirectory(child.Items))
                max = System.Math.Max(max, CountRings(child, level + 1));
        }
        return max;
    }

    private long ComputeDisplayedSizes(FsItem node)
    {
        long displayed = 0;
        if (node.Items is { Count: > 0 })
        {
            long filteredChildrenSize = 0;
            foreach (var child in node.Items)
            {
                if (IsAlwaysVisibleRootEntry(child))
                {
                    var size = System.Math.Max(0, child.Size);
                    _displayedSize[child] = size;
                    displayed += size;
                }
                else if (child.Size <= _filterThreshold)
                {
                    filteredChildrenSize += System.Math.Max(0, child.Size);
                }
                else
                {
                    displayed += ComputeDisplayedSizes(child);
                }
            }

            if (displayed == 0 && node.Size > _filterThreshold)
            {
                // All descendants are individually filtered, but this directory is still visible.
                displayed = System.Math.Max(0, node.Size);
            }
            else
            {
                _filteredTotal += filteredChildrenSize;
            }
        }
        else
        {
            displayed = System.Math.Max(0, node.Size);
        }

        _displayedSize[node] = displayed;
        return displayed;
    }

    private long DisplayedOf(FsItem item) =>
        _displayedSize.TryGetValue(item, out var value) ? value : 0;

    private double AddChildren(
        IReadOnlyList<FsItem> children,
        int level,
        double startAngle,
        double sweepAngle,
        SliceHsb? parentColor,
        long denominator,
        ref int sectorBudget)
    {
        var cursor = startAngle;
        if (denominator <= 0 || sectorBudget <= 0)
            return cursor;

        var order = Enumerable.Range(0, children.Count)
            .Where(i => DisplayedOf(children[i]) > 0)
            .OrderByDescending(i => DisplayedOf(children[i]))
            .ThenBy(i => children[i].Name, System.StringComparer.Ordinal)
            .ToArray();
        var visibleCount = order.Length;
        var rank = 0;

        for (; rank < visibleCount && sectorBudget > 0; rank++)
        {
            if (_segments.Count >= MaxSegments)
                break;

            var child = children[order[rank]];
            var childDisplayed = DisplayedOf(child);
            var childSweep = sweepAngle * childDisplayed / denominator;

            if (IsLayoutCollapsedFile(child, level))
            {
                cursor += childSweep;
                continue;
            }

            var remainingSiblings = visibleCount - rank - 1;
            if (remainingSiblings > 0 && sectorBudget == 1)
                break;

            var hsb = parentColor is null
                ? SliceColorPalette.LevelBaseColor(rank, visibleCount)
                : SliceColorPalette.ChildShade(parentColor.Value, rank, visibleCount);

            _segments.Add(new SunburstSegment(
                child,
                level,
                level,
                child.Size,
                cursor,
                childSweep,
                SegmentColor(child, hsb.ToColor())));
            sectorBudget--;

            if (ShouldRecurseInto(child, level))
            {
                if (level == 0)
                {
                    // Each root sector (top-level directory) gets its own budget covering the
                    // sector segment plus all of its descendants, so a large sector cannot drain
                    // the budget and leave sibling root sectors undrawn. The root segment itself
                    // already consumed one slot, so the subtree gets MaxSegmentsPerSector - 1.
                    var subtreeBudget = MaxSegmentsPerSector - 1;
                    AddChildren(child.Items!, level + 1, cursor, childSweep, hsb, childDisplayed, ref subtreeBudget);
                }
                else
                {
                    AddChildren(child.Items!, level + 1, cursor, childSweep, hsb, childDisplayed, ref sectorBudget);
                }
            }

            cursor += childSweep;
        }

        if (rank < visibleCount && _segments.Count < MaxSegments)
        {
            long otherSize = 0;
            long otherDisplayed = 0;
            for (var i = rank; i < visibleCount; i++)
            {
                var child = children[order[i]];
                otherSize += child.Size;
                otherDisplayed += DisplayedOf(child);
            }

            if (otherDisplayed > 0)
            {
                var otherSweep = sweepAngle * otherDisplayed / denominator;
                var otherNode = new FsItem(ChartDisplayMetadata.OtherName, otherSize, isDir: false);

                _segments.Add(new SunburstSegment(
                    otherNode,
                    level,
                    level,
                    otherSize,
                    cursor,
                    otherSweep,
                    SegmentColor(otherNode, OtherBandColor)));

                if (sectorBudget > 0)
                    sectorBudget--;
                cursor += otherSweep;
            }
        }

        return cursor;
    }

    private void AddFilteredRootSector(double startAngle)
    {
        var sweep = 360d - startAngle;
        if (sweep <= 0)
            return;

        _segments.Add(new SunburstSegment(
            new FsItem(ChartDisplayMetadata.FilteredName, _filteredTotal, isDir: false),
            0,
            0,
            _filteredTotal,
            startAngle,
            sweep,
            FilteredBandColor));
    }

    private bool IsLayoutCollapsedFile(FsItem child, int level) =>
        _ringCount > 1
        && level == _ringCount - 1
        && !IsRealDirectory(child)
        && !IsAlwaysVisibleRootEntry(child);

    private bool ShouldRecurseInto(FsItem child, int level) =>
        child.Items is { Count: > 0 }
        && IsRealDirectory(child)
        && level + 1 < _ringCount
        && (level + 2 < _ringCount || AnyVisibleRealDirectory(child.Items));

    private bool IsVisibleChild(FsItem child) =>
        child.Size > _filterThreshold || IsAlwaysVisibleRootEntry(child);

    private static bool IsRealDirectory(FsItem item) =>
        item.IsDir
        && item.Name is not (
            DriveScanMetadata.FreeSpaceName
            or DriveScanMetadata.InaccessibleName
            or ChartDisplayMetadata.FilteredName
            or ChartDisplayMetadata.OtherName);

    private bool AnyVisibleRealDirectory(IReadOnlyList<FsItem> children)
    {
        foreach (var child in children)
            if (IsVisibleChild(child) && IsRealDirectory(child))
                return true;
        return false;
    }

    private static bool IsAlwaysVisibleRootEntry(FsItem item) =>
        item.Name is DriveScanMetadata.FreeSpaceName or DriveScanMetadata.InaccessibleName;

    private static Color SegmentColor(FsItem item, Color paletteColor) =>
        item.Name switch
        {
            DriveScanMetadata.FreeSpaceName => FreeSpaceColor,
            DriveScanMetadata.InaccessibleName => InaccessibleColor,
            ChartDisplayMetadata.FilteredName => FilteredBandColor,
            ChartDisplayMetadata.OtherName => OtherBandColor,
            _ => paletteColor
        };

}
