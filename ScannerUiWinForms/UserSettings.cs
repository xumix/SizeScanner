// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Text.Json;

namespace ScannerUiWinForms;

internal sealed class UserSettings
{
    public int FilterIndex { get; set; } = 4;
    public int FreeSpaceIndex { get; set; } = 1;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int SplitterDistance { get; set; }
    public bool InaccessiblePaneCollapsed { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "SizeScanner", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UserSettings();
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
        }
        catch { return new UserSettings(); }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
