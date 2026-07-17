// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

public static class ChartNodeRules
{
    public static bool IsFreeSpace(FsItem? item) =>
        item?.Name == DriveScanMetadata.FreeSpaceName;

    public static bool IsFiltered(FsItem? item) =>
        item?.Name == ChartDisplayMetadata.FilteredName;

    public static bool IsInaccessible(FsItem? item) =>
        item?.Name == DriveScanMetadata.InaccessibleName;

    public static bool IsAggregate(FsItem? item) =>
        item?.Kind == FsItemKind.Aggregate;

    public static bool IsSyntheticSegment(FsItem? item) =>
        item is null
        || IsAggregate(item)
        || item.Name is DriveScanMetadata.FreeSpaceName
            or DriveScanMetadata.InaccessibleName
            or ChartDisplayMetadata.FilteredName
            or ChartDisplayMetadata.OtherName;

    public static bool IsAlwaysVisibleDriveEntry(FsItem item) =>
        item.Name is DriveScanMetadata.FreeSpaceName or DriveScanMetadata.InaccessibleName;

    public static bool IsRealDirectory(FsItem item) =>
        item.IsDir && !IsSyntheticSegment(item);

    public static bool SuppressesContextMenu(FsItem? item) =>
        item is null || IsSyntheticSegment(item);

    public static bool IsScopable(FsItem? item) =>
        item is { IsDir: true, Items: not null }
        && (item.Items.Count > 0 || item.HasUnretainedChildren)
        && !IsSyntheticSegment(item);
}
