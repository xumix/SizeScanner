// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Media;
using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

public sealed record SunburstSegment(
    FsItem? Node,
    int Level,
    int RingIndex,
    long Size,
    double StartAngle,
    double SweepAngle,
    Color Color)
{
    public double EndAngle => StartAngle + SweepAngle;
    public bool IsActionable => Node is not null;

    public string DisplayText =>
        $"{Node?.Name ?? "<no name>"} Size: ({Humanize.Size(Size)})";

    public override string ToString() =>
        $"{Node?.Name ?? "<no name>"} Size: ({Humanize.Size(Size)})";
}
