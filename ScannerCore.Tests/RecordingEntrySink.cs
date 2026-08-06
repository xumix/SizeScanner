// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using ScannerCore;

namespace ScannerCore.Tests;

/// <summary>
/// Captures everything a cursor reports so assertions can run after the shared native
/// buffer has been reused. Names must be copied out of the span here, not retained.
/// </summary>
internal sealed class RecordingEntrySink : IDirectoryEntrySink
{
    public List<(string Name, long Size, bool IsDirectory)> Entries { get; } = [];

    public long Size { get; private set; }

    public IEnumerable<string> Names => Entries.Select(entry => entry.Name);

    public void OnEntry(ReadOnlySpan<char> name, long size, bool isDirectory)
    {
        Entries.Add((name.ToString(), size, isDirectory));
        Size += size;
    }
}
