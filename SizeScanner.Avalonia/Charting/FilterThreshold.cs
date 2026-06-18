// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

public static class FilterThreshold
{
    private const float PercentPerIndexStep = 0.0025f;

    public static float PercentFromIndex(int filterIndex) =>
        PercentPerIndexStep * filterIndex;

    public static long Compute(float percent, FsItem displayRoot) =>
        (long)(percent * GetDisplayTotal(displayRoot));

    public static long GetDisplayTotal(FsItem displayRoot) =>
        displayRoot.Items is { Count: > 0 } items
            ? items.Sum(i => System.Math.Max(0, i.Size))
            : System.Math.Max(0, displayRoot.Size);

    /// <summary>
    /// Sum of immediate children excluding the synthetic <see cref="DriveScanMetadata.FreeSpaceName"/> entry.
    /// Used for the chart center label; chart arc layout still uses <see cref="GetDisplayTotal"/>.
    /// </summary>
    public static long GetUsedTotal(FsItem displayRoot) =>
        displayRoot.Items is { Count: > 0 } items
            ? items.Where(i => !ChartNodeRules.IsFreeSpace(i)).Sum(i => System.Math.Max(0, i.Size))
            : System.Math.Max(0, displayRoot.Size);
}
