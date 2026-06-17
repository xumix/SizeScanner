// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;

namespace ScannerCore;

/// <summary>
/// A scan engine builds an FsItem tree for a target. Implementations must be equivalent:
/// drive scans use allocation size; directory scans use logical size; reparse points are
/// skipped unless offline; access-denied directories yield Items == null and are recorded.
/// </summary>
public interface IScanEngine
{
    bool CanHandle(string target, bool isDriveScan, bool isElevated);

    ScanResult Scan(string target, bool isDriveScan, CancellationToken token, Action<string, long>? onProgress);
}

public sealed class ScanResult
{
    public required FsItem Root { get; init; }
    public required long Total { get; init; }
    public required IReadOnlyList<string> Inaccessible { get; init; }
}
