// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;

namespace SizeScanner.Avalonia.Abstractions;

public readonly record struct DeleteResult(bool Success, string? Error);

public interface IFileSystemActions
{
    void ShowInExplorer(string path);
    Task<DeleteResult> DeleteAsync(string path, bool permanent);
}
