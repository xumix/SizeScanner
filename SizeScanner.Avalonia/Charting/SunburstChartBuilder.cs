// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

/// <summary>
/// Builds a true sunburst layout: each segment has a ring, angular span, color, and optional FsItem.
/// Thresholded entries remain as transparent placeholders so sibling angles still add up correctly,
/// but they are not actionable and are ignored by hit-testing.
/// </summary>
public sealed class SunburstChartBuilder
{
    private long _filterThreshold;
    private readonly List<SunburstSegment> _segments = new();
    private int _ringCount;

    public SunburstChart Build(FsItem root, long filterThreshold)
    {
        _filterThreshold = filterThreshold;
        _segments.Clear();
        _ringCount = root.Items is { Count: > 0 } ? CountRings(root, level: 0) : 0;

        if (_ringCount == 0)
            return new SunburstChart([], 0, 0);

        var total = PositiveSum(root.Items!);
        if (total <= 0)
            return new SunburstChart([], 0, 0);

        AddChildren(root.Items!, level: 0, startAngle: 0d, sweepAngle: 360d, parentColor: null);
        return new SunburstChart(_segments, _ringCount, total);
    }

    private int CountRings(FsItem node, int level)
    {
        if (node.Items is not { Count: > 0 })
            return level;

        var max = level + 1;
        foreach (var child in node.Items)
            if (child.Size > _filterThreshold && child.Items is { Count: > 0 })
                max = System.Math.Max(max, CountRings(child, level + 1));
        return max;
    }

    private void AddChildren(
        IReadOnlyList<FsItem> children,
        int level,
        double startAngle,
        double sweepAngle,
        SliceHsb? parentColor)
    {
        var total = PositiveSum(children);
        if (total <= 0)
            return;

        var cursor = startAngle;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var childSweep = sweepAngle * System.Math.Max(0, child.Size) / total;
            var hsb = parentColor is null
                ? SliceColorPalette.LevelBaseColor(i, children.Count)
                : SliceColorPalette.ChildShade(parentColor.Value, i, children.Count);
            var isPlaceholder = child.Size <= _filterThreshold;
            var node = isPlaceholder ? null : child;
            var color = isPlaceholder ? Colors.Transparent : hsb.ToColor();

            _segments.Add(new SunburstSegment(
                node,
                level,
                level,
                child.Size,
                cursor,
                childSweep,
                color,
                isPlaceholder));

            if (!isPlaceholder && child.Items is { Count: > 0 })
                AddChildren(child.Items, level + 1, cursor, childSweep, hsb);

            cursor += childSweep;
        }
    }

    private static long PositiveSum(IEnumerable<FsItem> items) =>
        items.Sum(item => System.Math.Max(0, item.Size));
}
