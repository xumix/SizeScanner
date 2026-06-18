// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using ScannerCore;
using SizeScanner.Avalonia.Charting;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class FilterThresholdTests
{
    [Fact]
    public void GetDisplayTotal_sums_immediate_children_including_synthetic_entries()
    {
        var root = TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 25),
            TestTree.File("page.sys", 200));

        Assert.Equal(725, FilterThreshold.GetDisplayTotal(root));
    }

    [Fact]
    public void GetDisplayTotal_uses_stripped_root_without_free_space()
    {
        var scanRoot = TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 0),
            TestTree.File("page.sys", 200));

        var stripped = new FsItem(scanRoot.Name, scanRoot.Size, scanRoot.IsDir)
        {
            Items = scanRoot.Items!.Skip(DriveScanMetadata.SyntheticEntryCount).ToList()
        };

        Assert.Equal(200, FilterThreshold.GetDisplayTotal(stripped));
    }

    [Fact]
    public void GetUsedTotal_excludes_free_space_but_keeps_other_children()
    {
        var root = TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 25),
            TestTree.File("page.sys", 200));

        Assert.Equal(225, FilterThreshold.GetUsedTotal(root));
    }

    [Fact]
    public void Compute_returns_percent_times_display_total()
    {
        var root = TestTree.Dir("folder",
            TestTree.File("a", 600),
            TestTree.File("b", 400));

        Assert.Equal(10, FilterThreshold.Compute(0.01f, root));
    }
}
