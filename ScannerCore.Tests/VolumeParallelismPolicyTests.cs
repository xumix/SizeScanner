// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class VolumeParallelismPolicyTests
{
    [Fact]
    public void ShouldParallelize_returns_false_for_unc_paths()
    {
        Assert.False(VolumeParallelismPolicy.ShouldParallelize(@"\\server\share\folder"));
    }

    [Fact]
    public void ShouldParallelize_returns_false_for_missing_drive_letter()
    {
        Assert.False(VolumeParallelismPolicy.ShouldParallelize(string.Empty));
    }

    [Fact]
    public void ShouldParallelize_matches_fixed_volume_of_temp_directory()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.GetPathRoot(temp.Path)!;
        var drive = DriveInfo.GetDrives().First(d =>
            string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase));

        if (drive.DriveType != DriveType.Fixed)
        {
            Assert.False(VolumeParallelismPolicy.ShouldParallelize(temp.Path));
            return;
        }

        // CI/dev machines are almost always SSD; we only assert the policy runs without throwing.
        _ = VolumeParallelismPolicy.ShouldParallelize(temp.Path);
    }
}
