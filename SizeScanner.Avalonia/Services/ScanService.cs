// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

public sealed class ScanService : IScanService
{
    public string LastTarget { get; private set; } = string.Empty;
    public bool IsDriveScan { get; private set; }
    public DriveScanner Scanner { get; private set; } = new();

    public async Task<FsItem> RunAsync(
        string target,
        bool isDrive,
        CancellationToken cancellationToken,
        IProgress<ScanProgress> progress)
    {
        LastTarget = target;
        IsDriveScan = isDrive;
        Scanner = new DriveScanner();

        return await Task.Run(
            () => isDrive
                ? Scanner.ScanDrive(target, cancellationToken, progress)
                : Scanner.ScanDirectory(target, cancellationToken, progress),
            cancellationToken);
    }
}
