// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using SizeScanner.Avalonia.Models;
using SizeScanner.Avalonia.Services;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        using var dir = new TempDir();
        var store = new JsonSettingsStore(Path.Combine(dir.Path, "missing.json"));

        var settings = store.Load();

        Assert.Equal(4, settings.FilterIndex);
        Assert.Equal(1, settings.FreeSpaceIndex);
    }

    [Fact]
    public void Save_then_load_roundtrips_values()
    {
        using var dir = new TempDir();
        var store = new JsonSettingsStore(Path.Combine(dir.Path, "settings.json"));

        store.Save(new UserSettings
        {
            FilterIndex = 2,
            FreeSpaceIndex = 0,
            WindowWidth = 1234,
            WindowHeight = 567,
            SplitterDistance = 800,
            InaccessiblePaneCollapsed = true
        });

        var loaded = store.Load();

        Assert.Equal(2, loaded.FilterIndex);
        Assert.Equal(0, loaded.FreeSpaceIndex);
        Assert.Equal(1234, loaded.WindowWidth);
        Assert.Equal(567, loaded.WindowHeight);
        Assert.Equal(800, loaded.SplitterDistance);
        Assert.True(loaded.InaccessiblePaneCollapsed);
    }

    [Fact]
    public void Load_returns_defaults_on_corrupt_file()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{ not valid json");
        var store = new JsonSettingsStore(path);

        var settings = store.Load();

        Assert.Equal(4, settings.FilterIndex);
    }
}
