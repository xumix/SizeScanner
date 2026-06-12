// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.Models;

public sealed class UserSettings
{
    public int FilterIndex { get; set; } = 4;
    public int FreeSpaceIndex { get; set; } = 1;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int SplitterDistance { get; set; }
    public bool InaccessiblePaneCollapsed { get; set; }
}
