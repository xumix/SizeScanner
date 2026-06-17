// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

public static class ChartHoverToolTip
{
    public static string BuildText(IReadOnlyList<FsItem> itemsRootFirst)
    {
        if (itemsRootFirst.Count == 0)
            return string.Empty;

        var names = itemsRootFirst
            .Select((fsItem, depth) => BuildTreeName(fsItem.Name, depth))
            .ToArray();
        var nameColumnWidth = names.Max(n => n.Length);
        var lines = names
            .Select((name, index) =>
                $"{name.PadRight(nameColumnWidth)}  |  {Humanize.FsItem(itemsRootFirst[index])}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildTreeName(string name, int depth)
    {
        if (depth == 0)
            return name;

        return new string(' ', (depth - 1) * 4) + "` " + name;
    }
}
