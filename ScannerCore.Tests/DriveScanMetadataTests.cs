// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class DriveScanMetadataTests
{
    [Fact]
    public void Constants_match_existing_synthetic_entry_contract()
    {
        Assert.Equal("[Free space]", DriveScanMetadata.FreeSpaceName);
        Assert.Equal("[Inaccessible]", DriveScanMetadata.InaccessibleName);
        Assert.Equal(0, DriveScanMetadata.FreeSpaceIndex);
        Assert.Equal(1, DriveScanMetadata.InaccessibleIndex);
        Assert.Equal(2, DriveScanMetadata.SyntheticEntryCount);
    }

    [Fact]
    public void GetInaccessibleEntry_returns_second_synthetic_child()
    {
        var root = new FsItem("C:\\", 1000, isDir: true)
        {
            Items =
            [
                new FsItem(DriveScanMetadata.FreeSpaceName, 100, false),
                new FsItem(DriveScanMetadata.InaccessibleName, 50, false),
                new FsItem("Windows", 850, true)
            ]
        };

        var inaccessible = DriveScanMetadata.GetInaccessibleEntry(root);
        Assert.Equal(DriveScanMetadata.InaccessibleName, inaccessible.Name);
        Assert.Equal(50, inaccessible.Size);
    }

    [Fact]
    public void GetFreeSpaceEntry_returns_first_synthetic_child()
    {
        var root = new FsItem("C:\\", 1000, isDir: true)
        {
            Items =
            [
                new FsItem(DriveScanMetadata.FreeSpaceName, 100, false),
                new FsItem(DriveScanMetadata.InaccessibleName, 50, false),
                new FsItem("Windows", 850, true)
            ]
        };

        var freeSpace = DriveScanMetadata.GetFreeSpaceEntry(root);
        Assert.Equal(DriveScanMetadata.FreeSpaceName, freeSpace.Name);
        Assert.Equal(100, freeSpace.Size);
    }
}
