# SizeScanner — Agent Guide

Windows-only disk usage visualizer (.NET 10). Inspired by Steffen Gerlach's Scanner2; scans on demand and renders nested sunburst charts.

## Solution layout

| Project | Role |
|---------|------|
| `ScannerCore/` | Scan engine + `FsItem` tree model — **put all filesystem logic here** |
| `ScannerCore.Tests/` | xUnit unit and integration tests for `ScannerCore` |
| `SizeScanner.Avalonia/` | Production Avalonia UI (MVVM + custom sunburst chart); Windows-only due to ScannerCore |
| `SizeScanner.Avalonia.Tests/` | xUnit tests for Avalonia chart builder, services, and view-models |
| `ScannerConsole/` | Manual perf/progress harness only — not shipped |

Dependency flow: UI/Console/Tests → `ScannerCore`. Central package versions live in `Directory.Packages.props`. CI is defined in `.gitlab-ci.yml` (GitLab) and `.github/workflows/` (GitHub Actions); both require Windows runners.

## Core data flow

1. `DriveScanner.ScanDrive("C:", token, progress)` or `ScanDirectory(path, token, progress)` builds an `FsItem` tree (`ScannerCore/DriveScanner.cs`).
2. `DirectoryScanner` enumerates via `NtQueryDirectoryFile` P/Invoke (`DirectoryScanner.cs`) — not `Directory.GetFiles`.
3. Symlinks/reparse points are **skipped** unless `FILE_ATTRIBUTE_OFFLINE` (OneDrive online-only). Denied dirs return `null` → logged in `DriveScanner.Inaccessible`.
4. Drive scans prepend synthetic children via `DriveScanMetadata` (`[Free space]`, `[Inaccessible]`); directory scans do not.
5. UI maps the tree to nested sunburst segments via `SunburstChartBuilder`; each `SunburstSegment` holds the source `FsItem`. Items below the filter threshold collapse into a gray filtered band on the innermost ring.

## Windows / scanning specifics

- **Platform**: x86/x64 AnyCPU; requires Windows APIs (`kernel32.dll`, `ntdll.dll`). Not cross-platform. Requires a Windows version supported by .NET 10.
- **Size mode**: `ScanDrive` uses allocation size; `ScanDirectory` uses logical file size (`useAllocationSize` flag in `ScanUnitInternal`).
- **Progress**: `IProgress<ScanProgress>` callbacks from `DriveScanner` (throttled to 300 ms). UI wires `Progress<ScanProgress>` to the status bar and progress bar.
- **Cancellation**: Both `ScanDrive` and `ScanDirectory` accept `CancellationToken`.

## Build & run

Requires the **.NET 10 SDK** (pinned in `global.json`). SDK-style projects target `net10.0-windows`.

```powershell
dotnet restore SizeScanner.slnx
dotnet build SizeScanner.slnx -c Debug
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release
dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release
dotnet run --project .\SizeScanner.Avalonia\SizeScanner.Avalonia.csproj

# Scanner harness (path argument optional):
dotnet run --project .\ScannerConsole\ScannerConsole.csproj -- C:\some\folder

# Framework-dependent publish:
dotnet publish .\SizeScanner.Avalonia\SizeScanner.Avalonia.csproj -c Release -r win-x64 --self-contained false
```

**NuGet packages** (versions in `Directory.Packages.props`):
- `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` — Avalonia UI
- `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection` — MVVM and DI
- `Spectre.Console` — console harness output only
- `xunit.v3`, `Microsoft.NET.Test.Sdk`, `coverlet.collector` — test projects

## CI and release

| Platform | File(s) | Release trigger |
|----------|---------|-----------------|
| GitLab | `.gitlab-ci.yml` | Any git tag → `publish/` artifact (`tags: [windows]`) |
| GitHub | `.github/workflows/dotnet-desktop.yml`, `release.yml`, `codeql.yml` | Tag matching `v*` → GitHub Release with zip |

Both platforms: restore/build `SizeScanner.slnx` (Release), run `ScannerCore.Tests` and `SizeScanner.Avalonia.Tests` with coverlet, publish self-contained `win-x64` (`--self-contained true`).

## Conventions when editing

- Keep P/Invoke and native structs inside `DirectoryScanner` — do not scatter Win32 calls into UI.
- Avalonia UI lives in `SizeScanner.Avalonia/`; keep platform/IO behind interfaces in `Abstractions/` with Windows implementations in `Services/`. Chart building belongs in `Charting/SunburstChartBuilder.cs`; view-models in `ViewModels/`.
- Settings persist to `%AppData%\SizeScanner\settings.avalonia.json` via `JsonSettingsStore` and `Models/UserSettings.cs`.
- Synthetic drive entries: use `DriveScanMetadata` constants and helpers — do not hard-code `[Free space]` / `[Inaccessible]` or index `1`.
- Filter threshold: `0.0025f × FilterIndex × display-root total`, where display-root total is the sum of positive child sizes on `ChartViewModel.GetDisplayRoot()` (matches chart denominator via `FilterThreshold.GetDisplayTotal`).

## Key files

- `ScannerCore/DirectoryScanner.cs` — symlink/offline handling, native enumeration
- `ScannerCore/DriveScanner.cs` — recursion, progress, inaccessible tracking
- `ScannerCore/DriveScanMetadata.cs` — synthetic drive scan entry names and accessors
- `ScannerCore/ScanProgress.cs` — progress report record for `IProgress`
- `ScannerCore/FsItem.cs` — tree node (`Items` null = access denied dir)
- `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs` — sunburst segment building
- `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` — toolbar, scan orchestration, settings
- `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` — chart scope, hover, context actions
- `SizeScanner.Avalonia/Models/UserSettings.cs` — persisted settings DTO
- `SizeScanner.Avalonia/Services/JsonSettingsStore.cs` — JSON settings load/save
