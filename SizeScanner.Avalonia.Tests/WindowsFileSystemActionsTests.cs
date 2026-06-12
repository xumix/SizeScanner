// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using SizeScanner.Avalonia.Services;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class WindowsFileSystemActionsTests
{
    [Fact]
    public void TryDelete_permanent_removes_file_and_reports_success()
    {
        using var dir = new TempDir();
        var file = dir.CreateFile("doomed.bin", 16);
        var actions = new WindowsFileSystemActions();

        var ok = actions.TryDelete(file, permanent: true, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void TryDelete_missing_path_reports_failure()
    {
        using var dir = new TempDir();
        var actions = new WindowsFileSystemActions();

        var ok = actions.TryDelete(Path.Combine(dir.Path, "nope.bin"), permanent: true, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
