// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.Models;

/// <param name="Root">Two-character drive root, e.g. "C:".</param>
/// <param name="DisplayName">Label shown on the toolbar button, e.g. "C:\".</param>
public sealed record DriveItem(string Root, string DisplayName);
