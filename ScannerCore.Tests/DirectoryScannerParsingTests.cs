// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class DirectoryScannerParsingTests
{
    [Fact]
    public void Scan_returns_entries_with_logical_sizes_and_names()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("alpha.txt", 100);
        temp.CreateFile("beta.bin", 250);

        var scanner = new DirectoryScanner(preferAllocatedSize: false);
        var (entries, processed) = ScanAll(scanner, temp.Path + System.IO.Path.DirectorySeparatorChar);

        Assert.NotNull(entries);
        var byName = entries!.ToDictionary(e => e.Name, e => e.Size);
        Assert.Equal(100, byName["alpha.txt"]);
        Assert.Equal(250, byName["beta.bin"]);
        Assert.Equal(350, processed);
    }

    [Fact]
    public void Scan_excludes_dot_directories()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("only.txt", 1);

        var scanner = new DirectoryScanner(preferAllocatedSize: false);
        var (entries, _) = ScanAll(scanner, temp.Path + System.IO.Path.DirectorySeparatorChar);

        Assert.NotNull(entries);
        Assert.DoesNotContain(entries!, e => e.Name is "." or "..");
    }

    [Fact]
    public void Scan_returns_null_for_missing_directory()
    {
        var scanner = new DirectoryScanner(preferAllocatedSize: false);
        var (entries, _) = ScanAll(scanner, @"X:\does\not\exist\");
        Assert.Null(entries);
    }

    [Fact]
    public void Scan_is_safe_to_call_concurrently()
    {
        using var temp = new TemporaryDirectory();
        for (var i = 0; i < 50; i++) temp.CreateFile($"f{i}.dat", i + 1);
        var path = temp.Path + System.IO.Path.DirectorySeparatorChar;
        var scanner = new DirectoryScanner(preferAllocatedSize: false);

        var totals = new long[8];
        System.Threading.Tasks.Parallel.For(0, 8, i =>
        {
            var (entries, _) = ScanAll(scanner, path);
            totals[i] = entries!.Sum(e => e.Size);
        });

        Assert.All(totals, t => Assert.Equal(totals[0], t));
    }

    private static (List<FsItem>? Entries, long Processed) ScanAll(
        DirectoryScanner scanner, string dir)
    {
        var cursor = ((IDirectoryEntrySource)scanner).Open(dir);
        if (cursor is null)
            return (null, 0);

        using (cursor)
        {
            var sink = new CollectingSink();
            var rented = ArrayPool<byte>.Shared.Rent(DirectoryScanner.BufferSize);
            try
            {
                var buffer = rented.AsSpan(0, DirectoryScanner.BufferSize);
                while (cursor.ReadNext(buffer, sink) == DirectoryBatchResult.Entries)
                {
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            return (sink.Items, sink.Size);
        }
    }

    private sealed class CollectingSink : IDirectoryEntrySink
    {
        public List<FsItem> Items { get; } = new();
        public long Size { get; private set; }

        public void OnEntry(ReadOnlySpan<char> name, long size, bool isDirectory)
        {
            Items.Add(new FsItem(new string(name), size, isDirectory));
            Size += size;
        }
    }
}
