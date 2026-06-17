// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Text.Json;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _filePath;

    public JsonSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SizeScanner", "settings.avalonia.json"))
    {
    }

    public JsonSettingsStore(string filePath) => _filePath = filePath;

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new UserSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(_filePath), SizeScannerJsonContext.Default.UserSettings)
                ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, SizeScannerJsonContext.Default.UserSettings));
    }
}
