// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Media;
using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

public sealed record SunburstSegment(
    FsItem? Node,
    string DisplayName,
    int Level,
    int RingIndex,
    long Size,
    double StartAngle,
    double SweepAngle,
    Color Color)
{
    public double EndAngle => StartAngle + SweepAngle;

    /// <summary>
    /// Whether the segment takes part in hit-testing, and so can be hovered for a status
    /// path and tooltip. Synthetic bands such as <c>[Free space]</c> and <c>[Other]</c> are
    /// included: they are hoverable but not clickable. What a click may do is a separate
    /// question answered per node by <see cref="ChartNodeRules"/>.
    /// </summary>
    public bool IsHitTestable => Node is not null;

    public override string ToString() =>
        $"{DisplayName} Size: ({Humanize.Size(Size)})";
}
