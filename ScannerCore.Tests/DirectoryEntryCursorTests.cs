// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

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
        for (var i = 0; i < 32; i++)
            temp.CreateFile($"entry-{i:D2}.txt", 10);

        IDirectoryEntrySource scanner = new DirectoryScanner(preferAllocatedSize: false);
        using var cursor = scanner.Open(
            temp.Path + Path.DirectorySeparatorChar);
        Assert.NotNull(cursor);

        var sink = new RecordingEntrySink();
        var buffer = new byte[512];
        var batchCount = 0;
        while (cursor!.ReadNext(buffer, sink) == DirectoryBatchResult.Entries)
            batchCount++;

        Assert.True(batchCount >= 2, $"Expected multiple native batches, got {batchCount}.");
        Assert.Equal(320, sink.Size);
        Assert.Equal(32, sink.Entries.Count);
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

        var sink = new RecordingEntrySink();
        var buffer = new byte[DirectoryScanner.BufferSize];
        while (cursor!.ReadNext(buffer, sink) == DirectoryBatchResult.Entries)
        {
        }

        Assert.DoesNotContain(sink.Names, n => n is "." or "..");
    }
}
