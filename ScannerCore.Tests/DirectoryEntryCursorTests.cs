// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class DirectoryEntryCursorTests
{
    [Fact]
    public void Cursor_reports_entries_batch_by_batch()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("alpha.txt", 100);
        temp.CreateFile("beta.bin", 250);

        var scanner = new DirectoryScanner(preferAllocatedSize: false);
        using var cursor = scanner.Open(
            temp.Path + Path.DirectorySeparatorChar);
        Assert.NotNull(cursor);

        var sink = new RecordingSink();
        var buffer = new byte[DirectoryScanner.BufferSize];
        while (cursor!.ReadNext(buffer, sink) == DirectoryBatchResult.Entries)
        {
        }

        Assert.Equal(350, sink.Size);
        Assert.Equal(["alpha.txt", "beta.bin"], sink.Names.Order());
    }

    [Fact]
    public void Cursor_returns_null_for_missing_directory()
    {
        var scanner = new DirectoryScanner(preferAllocatedSize: false);
        using var cursor = scanner.Open(@"X:\does\not\exist\");
        Assert.Null(cursor);
    }

    [Fact]
    public void Cursor_excludes_dot_directories()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("only.txt", 1);

        var scanner = new DirectoryScanner(preferAllocatedSize: false);
        using var cursor = scanner.Open(
            temp.Path + Path.DirectorySeparatorChar);
        Assert.NotNull(cursor);

        var sink = new RecordingSink();
        var buffer = new byte[DirectoryScanner.BufferSize];
        while (cursor!.ReadNext(buffer, sink) == DirectoryBatchResult.Entries)
        {
        }

        Assert.DoesNotContain(sink.Names, n => n is "." or "..");
    }

    private sealed class RecordingSink : IDirectoryEntrySink
    {
        public List<string> Names { get; } = [];
        public long Size { get; private set; }

        public void OnEntry(
            ReadOnlySpan<char> name,
            long size,
            bool isDirectory)
        {
            Names.Add(name.ToString());
            Size += size;
        }
    }
}
