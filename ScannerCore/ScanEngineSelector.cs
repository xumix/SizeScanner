// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ScannerCore;

/// <summary>
/// Picks the first registered engine whose <see cref="IScanEngine.CanHandle"/> returns true and
/// runs it; if it throws, falls back to the next capable engine. The last registered engine must
/// always be capable (the directory walk engine) so a scan never fails for lack of an engine.
/// </summary>
public sealed class ScanEngineSelector : IScanEngine
{
    private readonly IReadOnlyList<IScanEngine> _engines;
    private readonly bool _isElevated;

    public ScanEngineSelector(IReadOnlyList<IScanEngine> engines, bool isElevated)
    {
        _engines = engines;
        _isElevated = isElevated;
    }

    public bool CanHandle(string target, bool isDriveScan, bool isElevated) => true;

    public ScanResult Scan(string target, bool isDriveScan, CancellationToken token, Action<string, long>? onProgress)
    {
        Exception? lastFailure = null;
        for (var i = 0; i < _engines.Count; i++)
        {
            var engine = _engines[i];
            if (!engine.CanHandle(target, isDriveScan, _isElevated))
                continue;
            try
            {
                return engine.Scan(target, isDriveScan, token, onProgress);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                Debug.WriteLine($"Scan engine {engine.GetType().Name} failed, trying fallback: {ex.Message}");
            }
        }

        throw lastFailure ?? new InvalidOperationException("No scan engine could handle the target.");
    }
}
