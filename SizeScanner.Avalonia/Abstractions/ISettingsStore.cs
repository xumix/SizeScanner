// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.Abstractions;

public interface ISettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}
