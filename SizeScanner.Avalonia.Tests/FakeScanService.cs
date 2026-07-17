// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Tests;

/// <summary>
/// Records root (<see cref="RunAsync"/>) vs scope (<see cref="RunScopeAsync"/>) calls
/// separately, matching the production contract that scope scans must not mutate
/// <see cref="LastTarget"/>, <see cref="IsDriveScan"/>, or <see cref="Scanner"/>.
/// </summary>
internal sealed class FakeScanService : IScanService
{
    public string LastTarget { get; private set; } = string.Empty;
    public bool IsDriveScan { get; private set; }
    public DriveScanner Scanner { get; } = new();

    public List<(string Target, bool IsDrive)> RootCalls { get; } = [];
    public List<string> ScopeCalls { get; } = [];

    public Func<string, bool, FsItem>? RootResult { get; set; }
    public Func<string, FsItem>? ScopeResult { get; set; }

    /// <summary>When set, <see cref="RunAsync"/> awaits this instead of completing immediately.</summary>
    public TaskCompletionSource<FsItem>? PendingRoot { get; set; }

    /// <summary>When set, <see cref="RunScopeAsync"/> awaits this instead of completing immediately.</summary>
    public TaskCompletionSource<FsItem>? PendingScope { get; set; }

    public Task<FsItem> RunAsync(
        string target,
        bool isDrive,
        CancellationToken cancellationToken,
        IProgress<ScanProgress> progress,
        ScanTreeBudget? budget = null)
    {
        LastTarget = target;
        IsDriveScan = isDrive;
        RootCalls.Add((target, isDrive));

        if (PendingRoot is not null)
            return PendingRoot.Task;

        cancellationToken.ThrowIfCancellationRequested();
        var result = RootResult?.Invoke(target, isDrive)
            ?? throw new InvalidOperationException("FakeScanService: no RootResult configured.");
        return Task.FromResult(result);
    }

    public Task<FsItem> RunScopeAsync(
        string target,
        CancellationToken cancellationToken,
        IProgress<ScanProgress> progress,
        ScanTreeBudget? budget = null)
    {
        ScopeCalls.Add(target);

        if (PendingScope is not null)
            return PendingScope.Task;

        cancellationToken.ThrowIfCancellationRequested();
        var result = ScopeResult?.Invoke(target)
            ?? throw new InvalidOperationException("FakeScanService: no ScopeResult configured.");
        return Task.FromResult(result);
    }
}
