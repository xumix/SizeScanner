// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Controls;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

public sealed class TopLevelProvider : ITopLevelProvider
{
    public TopLevel? TopLevel { get; private set; }
    public void Register(TopLevel topLevel) => TopLevel = topLevel;
}
