// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using ScannerCore;

namespace SizeScanner.Avalonia.Tests;

internal static class TestTree
{
    public static FsItem File(string name, long size) => new(name, size, isDir: false);

    public static FsItem Dir(string name, params FsItem[] children)
    {
        long size = 0;
        foreach (var c in children) size += c.Size;
        var dir = new FsItem(name, size, isDir: true);
        AttachChildren(dir, children);
        return dir;
    }

    private static void AttachChildren(FsItem parent, IReadOnlyList<FsItem> children)
    {
        var list = new List<FsItem>(children);
        parent.Items = list;
        foreach (var child in list)
            SetParent(child, parent);
    }

    private static void SetParent(FsItem child, FsItem parent)
    {
        typeof(FsItem).GetProperty(nameof(FsItem.Parent))!.SetValue(child, parent);
    }
}
