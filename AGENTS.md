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

Dependency flow: UI/Console/Tests → `ScannerCore`. Central package versions live in `Directory.Packages.props`. CI is in `.gitlab-ci.yml` and `.github/workflows/`; both require Windows runners.

## Core data flow

1. `DriveScanner.ScanDrive("C:", token, progress)` or `ScanDirectory(path, token, progress)` builds an `FsItem` tree through `ScanEngineSelector` and `DirectoryWalkEngine`.
2. `DirectoryScanner` enumerates via `NtQueryDirectoryFile` P/Invoke — not `Directory.GetFiles`.
3. Symlinks/reparse points are **skipped** unless `FILE_ATTRIBUTE_OFFLINE` (OneDrive online-only). Denied dirs return `null` → logged in `DriveScanner.Inaccessible`.
4. Drive scans prepend synthetic children via `DriveScanMetadata` (`[Free space]`, `[Inaccessible]`); directory scans do not.
5. UI maps the tree to capped/indexed nested sunburst segments via `SunburstChartBuilder`; each `SunburstSegment` holds the source `FsItem`. Items below the filter threshold collapse into a gray filtered band on the innermost ring; overflow segments aggregate into `[Other]`.

## Windows / scanning specifics

- **Platform**: x86/x64 AnyCPU; requires Windows APIs (`kernel32.dll`, `ntdll.dll`). Not cross-platform. Requires a Windows version supported by .NET 10.
- **Size mode**: drive scans use allocation size, directory scans use logical file size; `isDriveScan` flows from `DriveScanner` into `DirectoryScanner`'s `preferAllocatedSize` (allocation size vs `EndOfFile`).
- **Progress**: `IProgress<ScanProgress>` callbacks from `DriveScanner` (throttled to 300 ms). UI wires `Progress<ScanProgress>` to the status bar and progress bar.
- **Cancellation/failure**: Both `ScanDrive` and `ScanDirectory` accept `CancellationToken`. Caller cancellation takes precedence; otherwise fan-out records and rethrows the first internal non-cancellation failure only after observing all in-flight siblings, so no partial result escapes.
- **Parallelism**: directories fan their children out across a scan-wide slot budget for the first `ScanTreeBudget.ParallelFanOutLevels` levels — default `1` (root only); deeper subtrees walk their whole tree sequentially on one shared slot. `2` and `3` remain explicit, measured knobs, not defaults. `MaxDegreeOfParallelism` (default `0`, resolving to `Math.Min(Environment.ProcessorCount, 16)`) caps concurrent native reads and outstanding 1 MiB buffer rentals, not open-cursor count — a parent cursor can stay open across an awaited child. Fan-out only happens when `VolumeParallelismPolicy` detects no seek penalty (SSD/NVMe); HDDs and unknown volumes stay sequential.

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

# Release-like publish (single-file, trimmed, native AoT):
dotnet publish .\SizeScanner.Avalonia\SizeScanner.Avalonia.csproj -c Release -r win-x64
```

**NuGet packages** (versions in `Directory.Packages.props`):
- `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` — Avalonia UI
- `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection` — MVVM and DI
- `Spectre.Console` — console harness output only
- `MinVer` — Git tag-based assembly versioning (`v*` tags, `MinVerTagPrefix=v`)
- `xunit.v3`, `Microsoft.NET.Test.Sdk`, `coverlet.collector` — test projects

## CI and release

| Platform | File(s) | Release trigger |
|----------|---------|-----------------|
| GitLab | `.gitlab-ci.yml` | Any git tag → `publish/` artifact (`tags: [windows]`) |
| GitHub | `.github/workflows/dotnet-desktop.yml`, `release.yml`, `codeql.yml` | Tag matching `v*` → GitHub Release with zip |

Both platforms: restore/build `SizeScanner.slnx` (Release), run `ScannerCore.Tests` and `SizeScanner.Avalonia.Tests` with coverlet, publish `win-x64` release artifacts.

## Conventions when editing

- Keep P/Invoke and native structs in `ScannerCore` (`DirectoryScanner` enumeration, `VolumeParallelismPolicy` seek-penalty detection) — never call Win32 from the UI.
- Avalonia UI lives in `SizeScanner.Avalonia/`; keep platform/IO behind interfaces in `Abstractions/` with Windows implementations in `Services/`. Chart building belongs in `Charting/SunburstChartBuilder.cs`; view-models in `ViewModels/`.
- Settings persist to `%AppData%\SizeScanner\settings.avalonia.json` via `JsonSettingsStore` and `Models/UserSettings.cs`.
- The application must stay **native AoT and trimming compatible** (`PublishAot`, `PublishTrimmed`, `IsAotCompatible`). Avoid reflection/dynamic activation patterns unless they are explicitly annotated and tested with publish.
- Synthetic drive entries: use `DriveScanMetadata` constants/helpers; chart-only synthetic names and UI rules belong in `ChartDisplayMetadata` / `ChartNodeRules`. Do not hard-code `[Free space]`, `[Inaccessible]`, `[Filtered]`, `[Other]`, or synthetic indices.
- Filter threshold: use `FilterThreshold.PercentFromIndex()` and `FilterThreshold.GetDisplayTotal()`; do not duplicate the `0.0025f × FilterIndex` factor.
- Settings are loaded into `MainWindowViewModel` once and persisted through its in-memory `UserSettings` snapshot; views should not independently load settings.
- `ChartViewModel` owns scope, hover, context target, and delete commands; views should delegate policy decisions such as context-menu suppression to the VM/chart rules.

## Key files

- `ScannerCore/DirectoryScanner.cs` — symlink/offline handling, native enumeration
- `ScannerCore/DirectoryWalkEngine.cs` — scan tree walk, SSD-gated depth-limited fan-out under a shared slot budget
- `ScannerCore/VolumeParallelismPolicy.cs` — P/Invoke seek-penalty detection gating parallelism
- `ScannerCore/DriveScanner.cs` — scan orchestration, progress, inaccessible tracking
- `ScannerCore/DriveScanMetadata.cs` — synthetic drive scan entry names/accessors/insertion
- `ScannerCore/ScanProgress.cs` — progress report record for `IProgress`
- `ScannerCore/FsItem.cs` — tree node (`Items` null = access denied dir)
- `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs` — capped sunburst segment building
- `SizeScanner.Avalonia/Charting/ChartNodeRules.cs` — chart synthetic/scoping/context-menu rules
- `SizeScanner.Avalonia/Charting/SunburstHitTest.cs` / `SunburstChart.cs` — per-ring segment indexes and hit-testing
- `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` — toolbar, scan orchestration, settings
- `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` — chart scope, hover, context actions
- `SizeScanner.Avalonia/Models/UserSettings.cs` — persisted settings DTO
- `SizeScanner.Avalonia/Services/JsonSettingsStore.cs` — JSON settings load/save
