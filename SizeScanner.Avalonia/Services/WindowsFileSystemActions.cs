// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystemActions : IFileSystemActions
{
    public void ShowInExplorer(string path)
    {
        Process.Start("explorer.exe", "/select,\"" + path + "\"");
    }

    public Task<DeleteResult> DeleteAsync(string path, bool permanent) =>
        Task.Run(() => DeleteCore(path, permanent));

    private static DeleteResult DeleteCore(string path, bool permanent)
    {
        try
        {
            if (File.Exists(path))
            {
                if (permanent) File.Delete(path);
                else FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return new DeleteResult(true, null);
            }

            if (Directory.Exists(path))
            {
                if (permanent) Directory.Delete(path, recursive: true);
                else FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return new DeleteResult(true, null);
            }

            return new DeleteResult(false, "Object is already unavailable.");
        }
        catch (Exception ex)
        {
            return new DeleteResult(false, ex.Message);
        }
    }
}
