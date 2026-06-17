// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.Abstractions;

public interface IElevationService
{
    bool IsRunningAsAdministrator();

    /// <summary>Relaunches elevated. Returns false if the user cancelled the UAC prompt.</summary>
    bool TryRelaunchAsAdministrator(out string? error);
}
