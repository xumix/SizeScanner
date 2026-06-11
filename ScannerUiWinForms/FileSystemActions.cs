// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace ScannerUiWinForms;

internal static class FileSystemActions
{
    public static void ShowInExplorer(string path)
    {
        Process.Start("explorer.exe", "/select,\"" + path + "\"");
    }

    public static bool TryDelete(string path, bool permanent, out string? error)
    {
        error = null;

        try
        {
            if (File.Exists(path))
            {
                if (permanent)
                    File.Delete(path);
                else
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                return true;
            }

            if (Directory.Exists(path))
            {
                if (permanent)
                    Directory.Delete(path, recursive: true);
                else
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                return true;
            }

            error = "Object is already unavailable.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
