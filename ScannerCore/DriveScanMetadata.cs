// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace ScannerCore;

public static class DriveScanMetadata
{
    public const string FreeSpaceName = "[Free space]";
    public const string InaccessibleName = "[Inaccessible]";
    public const int FreeSpaceIndex = 0;
    public const int InaccessibleIndex = 1;
    public const int SyntheticEntryCount = 2;

    public static FsItem GetInaccessibleEntry(FsItem driveRoot) =>
        driveRoot.Items![InaccessibleIndex];

    public static FsItem GetFreeSpaceEntry(FsItem driveRoot) =>
        driveRoot.Items![FreeSpaceIndex];

    public static void PrependSyntheticEntries(FsItem driveRoot, FsItem freeSpace, FsItem inaccessible)
    {
        freeSpace.Parent = driveRoot;
        inaccessible.Parent = driveRoot;
        (driveRoot.Items ??= []).InsertRange(0, new[] { freeSpace, inaccessible });
    }
}
