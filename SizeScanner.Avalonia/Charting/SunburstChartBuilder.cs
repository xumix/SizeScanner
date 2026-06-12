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
    private static readonly Color FreeSpaceColor = Colors.White;
    private static readonly Color FilteredBandColor = Color.FromRgb(128, 128, 128);
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

        var total = PositiveSum(root.Items!);
        if (total <= 0)
            return new SunburstChart([], 0, 0);

        ComputeDisplayedSizes(root);
        var endAngle = AddChildren(
            root.Items!,
            level: 0,
            startAngle: 0d,
            sweepAngle: 360d,
            parentColor: null,
            denominator: total);
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
        long denominator)
    {
        var cursor = startAngle;
        if (denominator <= 0)
            return cursor;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var childDisplayed = DisplayedOf(child);
            if (childDisplayed <= 0)
                continue;

            var childSweep = sweepAngle * childDisplayed / denominator;
            var hsb = parentColor is null
                ? SliceColorPalette.LevelBaseColor(i, children.Count)
                : SliceColorPalette.ChildShade(parentColor.Value, i, children.Count);

            if (IsLayoutCollapsedFile(child, level))
            {
                cursor += childSweep;
                continue;
            }

            _segments.Add(new SunburstSegment(
                child,
                level,
                level,
                child.Size,
                cursor,
                childSweep,
                SegmentColor(child, hsb.ToColor())));

            if (ShouldRecurseInto(child, level))
                AddChildren(child.Items!, level + 1, cursor, childSweep, hsb, childDisplayed);

            cursor += childSweep;
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
            or ChartDisplayMetadata.FilteredName);

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
            _ => paletteColor
        };

    private static long PositiveSum(IEnumerable<FsItem> items) =>
        items.Sum(item => System.Math.Max(0, item.Size));
}
