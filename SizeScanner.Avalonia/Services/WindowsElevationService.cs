// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsElevationService : IElevationService
{
    public bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool TryRelaunchAsAdministrator(out string? error)
    {
        error = null;
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            error = "Could not determine the application path.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // user cancelled UAC
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
