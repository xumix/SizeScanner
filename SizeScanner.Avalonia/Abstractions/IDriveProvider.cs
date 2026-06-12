// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.Abstractions;

public interface IDriveProvider
{
    IReadOnlyList<DriveItem> GetReadyDrives();
}
