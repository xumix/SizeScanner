// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

public sealed class AvaloniaFolderPicker : IFolderPicker
{
    private readonly ITopLevelProvider _topLevel;

    public AvaloniaFolderPicker(ITopLevelProvider topLevel) => _topLevel = topLevel;

    public async Task<string?> PickFolderAsync(string title)
    {
        var top = _topLevel.TopLevel;
        if (top is null) return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        return folder?.TryGetLocalPath();
    }
}
