// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using ScannerCore;

namespace SizeScanner.Avalonia.Abstractions;

public interface IScanService
{
    string LastTarget { get; }
    bool IsDriveScan { get; }
    DriveScanner Scanner { get; }

    Task<FsItem> RunAsync(string target, bool isDrive, CancellationToken cancellationToken, IProgress<ScanProgress> progress);
}
