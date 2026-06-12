// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;

namespace SizeScanner.Avalonia.Abstractions;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowInfoAsync(string title, string message);
}
