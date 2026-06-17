// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.IO;
using System.Linq;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.Services;

public sealed class DriveProvider : IDriveProvider
{
    public IReadOnlyList<DriveItem> GetReadyDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new DriveItem(d.Name[..2], d.Name))
            .ToList();
}
