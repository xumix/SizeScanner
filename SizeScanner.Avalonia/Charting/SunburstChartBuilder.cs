// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
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
        var endAngle = AddTopLevel(root, total);
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
            if (!ChartNodeRules.IsRealDirectory(child) || child.Items is not { Count: > 0 })
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
                if (ChartNodeRules.IsAlwaysVisibleDriveEntry(child))
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

    private double AddTopLevel(FsItem root, long total)
    {
        var sectorBudget = MaxSegmentsPerSector;
        var rootParent = new PendingParent(root, total, 0d, 360d, null, 0);
        var sectors = EmitRing([rootParent], ring: 0, ref sectorBudget);
        var endAngle = RingEndAngle(0);

        foreach (var sector in sectors)
        {
            var subtreeBudget = MaxSegmentsPerSector - 1;
            ExpandSector(sector, ref subtreeBudget);
        }

        return endAngle;
    }

    private readonly record struct PendingParent(
        FsItem Node,
        long Denominator,
        double StartAngle,
        double Sweep,
        SliceHsb? Color,
        int ChildRing);

    private sealed class ParentRingState
    {
        public double Cursor;
        public int EmittedCount;
        public int VisibleCount;
        public int ChildRank;
        public long EmittedDisplayed;
        public long EmittedSize;
        public long TotalDisplayed;
        public long TotalSize;
    }

    private void ExpandSector(PendingParent sector, ref int budget)
    {
        var frontier = new List<PendingParent> { sector };

        while (frontier.Count > 0 && budget > 0 && _segments.Count < MaxSegments)
            frontier = EmitRing(frontier, frontier[0].ChildRing, ref budget);
    }

    private List<PendingParent> EmitRing(IReadOnlyList<PendingParent> parents, int ring, ref int budget)
    {
        var candidates = new List<(int ParentIndex, FsItem Child)>();
        var parentStates = new ParentRingState[parents.Count];

        for (var p = 0; p < parents.Count; p++)
        {
            var parent = parents[p];
            var state = new ParentRingState { Cursor = parent.StartAngle };
            if (parent.Node.Items is { Count: > 0 } children)
            {
                foreach (var child in children)
                {
                    var displayed = DisplayedOf(child);
                    if (displayed <= 0)
                        continue;

                    candidates.Add((p, child));
                    state.VisibleCount++;
                    state.TotalDisplayed += displayed;
                    state.TotalSize += child.Size;
                }
            }

            parentStates[p] = state;
        }

        if (candidates.Count == 0)
            return [];

        candidates.Sort((a, b) =>
        {
            var cmp = DisplayedOf(b.Child).CompareTo(DisplayedOf(a.Child));
            return cmp != 0
                ? cmp
                : string.Compare(a.Child.Name, b.Child.Name, StringComparison.Ordinal);
        });

        var nextFrontier = new List<PendingParent>();

        foreach (var (parentIndex, child) in candidates)
        {
            if (budget <= 0 || _segments.Count >= MaxSegments)
                break;

            var parent = parents[parentIndex];
            var state = parentStates[parentIndex];
            var childDisplayed = DisplayedOf(child);
            var childSweep = parent.Sweep * childDisplayed / parent.Denominator;

            if (IsLayoutCollapsedFile(child, ring))
            {
                state.ChildRank++;
                state.Cursor += childSweep;
                continue;
            }

            var remainingAtParent = state.VisibleCount - state.ChildRank;
            if (remainingAtParent > 1 && budget == 1)
                break;

            var hsb = SegmentHsb(parent, state.ChildRank, state.VisibleCount);
            state.ChildRank++;

            _segments.Add(new SunburstSegment(
                child,
                ring,
                ring,
                child.Size,
                state.Cursor,
                childSweep,
                SegmentColor(child, hsb.ToColor())));

            state.Cursor += childSweep;
            state.EmittedCount++;
            state.EmittedDisplayed += childDisplayed;
            state.EmittedSize += child.Size;
            budget--;

            if (ShouldRecurseInto(child, ring))
            {
                nextFrontier.Add(new PendingParent(
                    child,
                    childDisplayed,
                    state.Cursor - childSweep,
                    childSweep,
                    hsb,
                    ring + 1));
            }
        }

        AddOtherSegments(parents, parentStates, ring, ref budget);
        return nextFrontier;
    }

    private SliceHsb SegmentHsb(PendingParent parent, int childRank, int visibleCount) =>
        parent.Color is { } color
            ? SliceColorPalette.ChildShade(color, childRank, visibleCount)
            : SliceColorPalette.LevelBaseColor(childRank, visibleCount);

    private void AddOtherSegments(
        IReadOnlyList<PendingParent> parents,
        IReadOnlyList<ParentRingState> parentStates,
        int ring,
        ref int budget)
    {
        for (var p = 0; p < parents.Count; p++)
        {
            if (_segments.Count >= MaxSegments)
                break;

            var parent = parents[p];
            var state = parentStates[p];
            if (state.EmittedCount < 1)
                continue;

            var otherDisplayed = state.TotalDisplayed - state.EmittedDisplayed;
            if (otherDisplayed <= 0)
                continue;

            var otherSize = state.TotalSize - state.EmittedSize;
            var otherSweep = parent.Sweep * otherDisplayed / parent.Denominator;
            var otherNode = new FsItem(ChartDisplayMetadata.OtherName, otherSize, isDir: false);

            _segments.Add(new SunburstSegment(
                otherNode,
                ring,
                ring,
                otherSize,
                state.Cursor,
                otherSweep,
                SegmentColor(otherNode, OtherBandColor)));

            if (budget > 0)
                budget--;
            state.Cursor += otherSweep;
        }
    }

    private double RingEndAngle(int ring)
    {
        var endAngle = 0d;
        foreach (var segment in _segments)
            if (segment.RingIndex == ring && segment.EndAngle > endAngle)
                endAngle = segment.EndAngle;
        return endAngle;
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
        && !ChartNodeRules.IsRealDirectory(child)
        && !ChartNodeRules.IsAlwaysVisibleDriveEntry(child);

    private bool ShouldRecurseInto(FsItem child, int level) =>
        child.Items is { Count: > 0 }
        && ChartNodeRules.IsRealDirectory(child)
        && level + 1 < _ringCount
        && (level + 2 < _ringCount || AnyVisibleRealDirectory(child.Items));

    private bool IsVisibleChild(FsItem child) =>
        child.Size > _filterThreshold || ChartNodeRules.IsAlwaysVisibleDriveEntry(child);

    private bool AnyVisibleRealDirectory(IReadOnlyList<FsItem> children)
    {
        foreach (var child in children)
            if (IsVisibleChild(child) && ChartNodeRules.IsRealDirectory(child))
                return true;
        return false;
    }

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
