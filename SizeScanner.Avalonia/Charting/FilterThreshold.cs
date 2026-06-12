// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

public static class FilterThreshold
{
    public static long Compute(float percent, FsItem displayRoot) =>
        (long)(percent * GetDisplayTotal(displayRoot));

    public static long GetDisplayTotal(FsItem displayRoot) =>
        displayRoot.Items is { Count: > 0 } items
            ? items.Sum(i => System.Math.Max(0, i.Size))
            : System.Math.Max(0, displayRoot.Size);
}
