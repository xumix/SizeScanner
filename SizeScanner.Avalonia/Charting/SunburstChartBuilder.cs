// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
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
        var endAngle = AddTopLevel(root.Items!, total);
        if (_filteredTotal > 0)
            AddFilteredRootSector(endAngle);

        // _ringCount above is the planned depth used while laying out (collapse/recurse
        // decisions). The per-sector budget and [Other] aggregation can stop the layout
        // short of that depth, so the reported ring count reflects the rings actually drawn,
        // preventing empty outer rings from cramping the visible ones.
        var drawnRingCount = ComputeDrawnRingCount();
        if (drawnRingCount == 0)
            return new SunburstChart([], 0, 0);

        return new SunburstChart(_segments, drawnRingCount, total);
    }

    private int ComputeDrawnRingCount()
    {
        var maxRing = -1;
        foreach (var segment in _segments)
            if (segment.RingIndex > maxRing)
                maxRing = segment.RingIndex;
        return maxRing + 1;
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

    private double AddTopLevel(IReadOnlyList<FsItem> children, long total)
    {
        var cursor = 0d;
        var sectorBudget = MaxSegmentsPerSector;
        if (total <= 0 || sectorBudget <= 0)
            return cursor;

        var order = Enumerable.Range(0, children.Count)
            .Where(i => DisplayedOf(children[i]) > 0)
            .OrderByDescending(i => DisplayedOf(children[i]))
            .ThenBy(i => children[i].Name, StringComparer.Ordinal)
            .ToArray();
        var visibleCount = order.Length;
        var rank = 0;

        for (; rank < visibleCount && sectorBudget > 0; rank++)
        {
            if (_segments.Count >= MaxSegments)
                break;

            var child = children[order[rank]];
            var childDisplayed = DisplayedOf(child);
            var childSweep = 360d * childDisplayed / total;

            if (IsLayoutCollapsedFile(child, level: 0))
            {
                cursor += childSweep;
                continue;
            }

            var remainingSiblings = visibleCount - rank - 1;
            if (remainingSiblings > 0 && sectorBudget == 1)
                break;

            var hsb = SliceColorPalette.LevelBaseColor(rank, visibleCount);

            _segments.Add(new SunburstSegment(
                child,
                0,
                0,
                child.Size,
                cursor,
                childSweep,
                SegmentColor(child, hsb.ToColor())));
            sectorBudget--;

            if (ShouldRecurseInto(child, level: 0))
            {
                // Each root sector gets its own budget covering all descendants. The root
                // segment already consumed one slot, so the subtree gets MaxSegmentsPerSector - 1.
                var subtreeBudget = MaxSegmentsPerSector - 1;
                ExpandSector(child, cursor, childSweep, hsb, ref subtreeBudget);
            }

            cursor += childSweep;
        }

        if (rank < visibleCount && _segments.Count < MaxSegments)
            cursor = AddOtherSector(children, order, rank, visibleCount, level: 0, cursor, 360d, total, ref sectorBudget);

        return cursor;
    }

    private readonly record struct PendingParent(
        FsItem Node,
        long Denominator,
        double StartAngle,
        double Sweep,
        SliceHsb Color,
        int ChildRing);

    private sealed class ParentRingState
    {
        public double Cursor;
        public int EmittedCount;
        public int VisibleCount;
        public int ChildRank;
        public readonly HashSet<FsItem> EmittedChildren = new(ReferenceEqualityComparer.Instance);
        public readonly HashSet<FsItem> ProcessedChildren = new(ReferenceEqualityComparer.Instance);
    }

    private void ExpandSector(
        FsItem sectorNode,
        double startAngle,
        double sweep,
        SliceHsb parentColor,
        ref int budget)
    {
        if (budget <= 0 || sectorNode.Items is not { Count: > 0 })
            return;

        var frontier = new List<PendingParent>
        {
            new(sectorNode, DisplayedOf(sectorNode), startAngle, sweep, parentColor, 1)
        };

        while (frontier.Count > 0 && budget > 0 && _segments.Count < MaxSegments)
        {
            var childRing = frontier[0].ChildRing;
            if (childRing >= _ringCount)
                break;

            var candidates = new List<(int ParentIndex, FsItem Child)>();
            for (var p = 0; p < frontier.Count; p++)
            {
                var parent = frontier[p];
                foreach (var child in parent.Node.Items!)
                {
                    if (DisplayedOf(child) > 0)
                        candidates.Add((p, child));
                }
            }

            if (candidates.Count == 0)
                break;

            candidates.Sort((a, b) =>
            {
                var cmp = DisplayedOf(b.Child).CompareTo(DisplayedOf(a.Child));
                return cmp != 0
                    ? cmp
                    : string.Compare(a.Child.Name, b.Child.Name, StringComparison.Ordinal);
            });

            var parentStates = new ParentRingState[frontier.Count];
            for (var p = 0; p < frontier.Count; p++)
            {
                var visibleCount = 0;
                foreach (var child in frontier[p].Node.Items!)
                {
                    if (DisplayedOf(child) > 0)
                        visibleCount++;
                }

                parentStates[p] = new ParentRingState
                {
                    Cursor = frontier[p].StartAngle,
                    VisibleCount = visibleCount
                };
            }

            var nextFrontier = new List<PendingParent>();

            foreach (var (parentIndex, child) in candidates)
            {
                if (budget <= 0 || _segments.Count >= MaxSegments)
                    break;

                var parent = frontier[parentIndex];
                var state = parentStates[parentIndex];
                if (state.ProcessedChildren.Contains(child))
                    continue;

                var childDisplayed = DisplayedOf(child);
                var childSweep = parent.Sweep * childDisplayed / parent.Denominator;

                if (IsLayoutCollapsedFile(child, childRing))
                {
                    state.ProcessedChildren.Add(child);
                    state.ChildRank++;
                    state.Cursor += childSweep;
                    continue;
                }

                var unprocessedAtParent = CountUnprocessedVisible(parent.Node, state);
                if (unprocessedAtParent > 1 && budget == 1)
                    break;

                var hsb = SliceColorPalette.ChildShade(parent.Color, state.ChildRank, state.VisibleCount);
                state.ChildRank++;
                state.ProcessedChildren.Add(child);

                _segments.Add(new SunburstSegment(
                    child,
                    childRing,
                    childRing,
                    child.Size,
                    state.Cursor,
                    childSweep,
                    SegmentColor(child, hsb.ToColor())));
                state.Cursor += childSweep;
                state.EmittedCount++;
                state.EmittedChildren.Add(child);
                budget--;

                if (ShouldRecurseInto(child, childRing))
                {
                    nextFrontier.Add(new PendingParent(
                        child,
                        childDisplayed,
                        state.Cursor - childSweep,
                        childSweep,
                        hsb,
                        childRing + 1));
                }
            }

            for (var p = 0; p < frontier.Count; p++)
            {
                if (_segments.Count >= MaxSegments)
                    break;

                var parent = frontier[p];
                var state = parentStates[p];
                if (state.EmittedCount < 1)
                    continue;

                long otherSize = 0;
                long otherDisplayed = 0;
                foreach (var child in parent.Node.Items!)
                {
                    if (DisplayedOf(child) <= 0 || state.EmittedChildren.Contains(child))
                        continue;

                    otherSize += child.Size;
                    otherDisplayed += DisplayedOf(child);
                }

                if (otherDisplayed <= 0)
                    continue;

                var otherSweep = parent.Sweep * otherDisplayed / parent.Denominator;
                var otherNode = new FsItem(ChartDisplayMetadata.OtherName, otherSize, isDir: false);

                _segments.Add(new SunburstSegment(
                    otherNode,
                    childRing,
                    childRing,
                    otherSize,
                    state.Cursor,
                    otherSweep,
                    SegmentColor(otherNode, OtherBandColor)));

                if (budget > 0)
                    budget--;
                state.Cursor += otherSweep;
            }

            frontier = nextFrontier;
        }
    }

    private int CountUnprocessedVisible(FsItem parent, ParentRingState state)
    {
        var count = 0;
        foreach (var child in parent.Items!)
        {
            if (DisplayedOf(child) > 0 && !state.ProcessedChildren.Contains(child))
                count++;
        }
        return count;
    }

    private double AddOtherSector(
        IReadOnlyList<FsItem> children,
        int[] order,
        int startRank,
        int visibleCount,
        int level,
        double cursor,
        double sweepAngle,
        long denominator,
        ref int sectorBudget)
    {
        long otherSize = 0;
        long otherDisplayed = 0;
        for (var i = startRank; i < visibleCount; i++)
        {
            var child = children[order[i]];
            otherSize += child.Size;
            otherDisplayed += DisplayedOf(child);
        }

        if (otherDisplayed <= 0)
            return cursor;

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

        return cursor + otherSweep;
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
