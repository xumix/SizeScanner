// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Controls;

namespace SizeScanner.Avalonia.Abstractions;

public interface ITopLevelProvider
{
    TopLevel? TopLevel { get; }
    void Register(TopLevel topLevel);
}
