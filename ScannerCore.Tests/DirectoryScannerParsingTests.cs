// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

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
        long processed = 0;
        var entries = scanner.Scan(temp.Path + System.IO.Path.DirectorySeparatorChar, ref processed);

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
        long processed = 0;
        var entries = scanner.Scan(temp.Path + System.IO.Path.DirectorySeparatorChar, ref processed);

        Assert.NotNull(entries);
        Assert.DoesNotContain(entries!, e => e.Name is "." or "..");
    }

    [Fact]
    public void Scan_returns_null_for_missing_directory()
    {
        var scanner = new DirectoryScanner(preferAllocatedSize: false);
        long processed = 0;
        var entries = scanner.Scan(@"X:\does\not\exist\", ref processed);
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
            long processed = 0;
            var entries = scanner.Scan(path, ref processed);
            totals[i] = entries!.Sum(e => e.Size);
        });

        Assert.All(totals, t => Assert.Equal(totals[0], t));
    }
}
