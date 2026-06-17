// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using System.Threading.Tasks;
using SizeScanner.Avalonia.Services;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class WindowsFileSystemActionsTests
{
    [Fact]
    public async Task DeleteAsync_permanent_removes_file_and_reports_success()
    {
        using var dir = new TempDir();
        var file = dir.CreateFile("doomed.bin", 16);
        var actions = new WindowsFileSystemActions();

        var result = await actions.DeleteAsync(file, permanent: true);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task DeleteAsync_missing_path_reports_failure()
    {
        using var dir = new TempDir();
        var actions = new WindowsFileSystemActions();

        var result = await actions.DeleteAsync(Path.Combine(dir.Path, "nope.bin"), permanent: true);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
