// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;

namespace SizeScanner.Avalonia.Abstractions;

public interface IFolderPicker
{
    /// <summary>Returns the selected folder path, or null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);
}
