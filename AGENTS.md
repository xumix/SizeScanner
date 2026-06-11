# SizeScanner — Agent Guide

Windows-only disk usage visualizer (.NET 10). Inspired by Steffen Gerlach's Scanner2; scans on demand and renders nested doughnut charts.

## Solution layout

| Project | Role |
|---------|------|
| `ScannerCore/` | Scan engine + `FsItem` tree model — **put all filesystem logic here** |
| `ScannerUiWinForms/` | Production WinForms UI (`Form1.cs` = chart/navigation; `Form1.Designer.cs` = controls) |
| `ScannerConsole/` | Manual perf/progress harness only — not shipped |

Dependency flow: UI/Console → `ScannerCore`. Central package versions live in `Directory.Packages.props`. No test project, no CI.

## Core data flow

1. `DriveScanner.ScanDrive("C:")` or `ScanDirectory(path, token)` builds an `FsItem` tree (`ScannerCore/DriveScanner.cs`).
2. `DirectoryScanner` enumerates via `NtQueryDirectoryFile` P/Invoke (`DirectoryScanner.cs`) — not `Directory.GetFiles`.
3. Symlinks/reparse points are **skipped** unless `FILE_ATTRIBUTE_OFFLINE` (OneDrive online-only). Denied dirs return `null` → logged in `DriveScanner.Inaccessible`.
4. Drive scans prepend synthetic children `[Free space]` and `[Inaccessible]`; directory scans do not.
5. UI maps the tree to nested `SeriesChartType.Doughnut` series; each `DataPoint.Tag` holds the source `FsItem`. Items below `_filterThreshold` collapse into placeholder slices (`PlaceholderTag` in `Form1.cs`).

## Windows / scanning specifics

- **Platform**: x86/x64 AnyCPU; requires Windows APIs (`kernel32.dll`, `ntdll.dll`). Not cross-platform. Requires a Windows version supported by .NET 10.
- **Size mode**: `ScanDrive` uses allocation size; `ScanDirectory` uses logical file size (`useAllocationSize` flag in `ScanUnitInternal`).
- **Progress**: Poll `DriveScanner.CurrentScanned` and `Progress` (bytes processed / occupied space). UI uses `Task.Run` + `timer1` (300 ms).
- **Cancellation**: `ScanDirectory` accepts `CancellationToken`; `ScanDrive` does not.

## Build & run

Requires the **.NET 10 SDK** (SDK-style projects targeting `net10.0-windows`).

```powershell
dotnet restore SizeScanner.sln
dotnet build SizeScanner.sln -c Debug
dotnet run --project .\ScannerUiWinForms\ScannerUiWinForms.csproj

# Scanner harness (path argument optional):
dotnet run --project .\ScannerConsole\ScannerConsole.csproj -- C:\some\folder

# Framework-dependent publish:
dotnet publish .\ScannerUiWinForms\ScannerUiWinForms.csproj -c Release -r win-x64 --self-contained false
```

**NuGet packages** (versions in `Directory.Packages.props`):
- `WinForms.DataVisualization` — chart control for `ScannerUiWinForms`
- `Spectre.Console` — console harness output only

No automated tests exist. Validate scanner changes via `ScannerConsole`; validate chart/filter behavior via WinForms UI.

## Conventions when editing

- Keep P/Invoke and native structs inside `DirectoryScanner` — do not scatter Win32 calls into UI.
- WinForms: event handlers and chart logic in `Form1.cs`; control layout/properties in `Form1.Designer.cs` only.
- `Humanize` (`Humanize.cs`) is the single place for size display strings (`"<Access Denied>"`, `"<Empty>"`, KB/MB suffixes).
- Synthetic `FsItem` names (`[Free space]`, `[Inaccessible]`) and index assumptions (`root.Items[1]` for inaccessible label) are intentional — preserve ordering when changing drive scan output.
- Filter threshold: `0.0025f * toolStripComboBox2.SelectedIndex` × total/occupied bytes (`GetDisplayThreshold`).

## Key files

- `ScannerCore/DirectoryScanner.cs` — symlink/offline handling, native enumeration
- `ScannerCore/DriveScanner.cs` — recursion, progress, inaccessible tracking
- `ScannerCore/FsItem.cs` — tree node (`Items` null = access denied dir)
- `ScannerUiWinForms/Form1.cs` — chart rendering, tooltips, Explorer open/delete shortcuts
