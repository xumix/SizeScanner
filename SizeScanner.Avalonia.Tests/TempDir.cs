// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;

namespace SizeScanner.Avalonia.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SizeScanner.Tests." + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path);
    }

    public string CreateFile(string relativePath, int sizeBytes)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        var dir = System.IO.Path.GetDirectoryName(fullPath)!;
        System.IO.Directory.CreateDirectory(dir);
        using var fs = System.IO.File.Create(fullPath);
        if (sizeBytes > 0)
            fs.SetLength(sizeBytes);
        return fullPath;
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Path, recursive: true); }
        catch { /* best effort */ }
    }
}
