// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.Abstractions;

public interface IFileSystemActions
{
    void ShowInExplorer(string path);
    bool TryDelete(string path, bool permanent, out string? error);
}
