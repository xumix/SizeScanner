# SizeScanner — Agent Guide

Windows-only, offline disk-usage visualizer built with .NET 10 and Avalonia. It scans local filesystems on demand, computes exact reachable byte totals, retains a bounded `FsItem` snapshot, and renders nested sunburst charts.

## Solution layout

| Project | Role |
|---------|------|
| `ScannerCore/` | Scan domain, native Windows enumeration, bounded walker, and `FsItem` model — **put all filesystem logic here** |
| `ScannerCore.Tests/` | xUnit tests for native parsing, scan semantics, bounds, cancellation, and parallelism |
| `SizeScanner.Avalonia/` | Production Avalonia UI: MVVM workflows, services, and custom sunburst chart |
| `SizeScanner.Avalonia.Tests/` | xUnit tests for charting, view-models, settings, and service adapters |
| `ScannerConsole/` | Manual perf/progress harness only — not shipped |

Dependency flow is acyclic: UI/Console/Tests → `ScannerCore`; `ScannerCore` never depends on Avalonia. Central package versions live in `Directory.Packages.props`. Canonical CI is under `.github/workflows/` and requires Windows runners.

## Core data flow

1. `MainWindowViewModel` calls `IScanService`; `ScanService` moves synchronous core scans off the UI thread.
2. `DriveScanner.ScanDrive(...)` or `ScanDirectory(...)` delegates through `ScanEngineSelector` to `DirectoryWalkEngine`.
3. `DirectoryScanner` streams `FILE_DIRECTORY_INFORMATION` batches from `NtQueryDirectoryFile` through the cursor/sink contracts — not `Directory.GetFiles`.
4. `BoundedDirectoryWalker` computes exact totals while retaining only a depth/width/node-bounded snapshot. Hidden entries become aggregate children and set `HasUnretainedChildren`; do not assume `FsItem.Items` is a complete directory listing.
5. Drive scans prepend synthetic children through `DriveScanMetadata` (`[Free space]`, `[Inaccessible]`); directory scans do not.
6. `ChartViewModel` passes the selected display root to `SunburstChartBuilder`, which produces capped, filtered, ring-indexed segments. Scoped drill-down re-scans the selected directory instead of retaining unbounded navigation history.

## Windows / scanning specifics

- **Platform**: Windows 10+ x64, targeting `net10.0-windows` and `win-x64`; requires `kernel32.dll` and `ntdll.dll`. The solution advertises additional CPU configurations, but only x64 is validated and published.
- **Size mode**: drive scans use allocation size, directory scans use logical file size; `isDriveScan` flows from `DriveScanner` into `DirectoryScanner`'s `preferAllocatedSize` (allocation size vs `EndOfFile`).
- **Progress**: `IProgress<ScanProgress>` callbacks from `DriveScanner` (throttled to 300 ms). UI wires `Progress<ScanProgress>` to the status bar and progress bar.
- **Cancellation/failure**: Both `ScanDrive` and `ScanDirectory` accept `CancellationToken`. Caller cancellation takes precedence; otherwise fan-out records and rethrows the first internal non-cancellation failure only after observing all in-flight siblings, so no partial result escapes.
- **Unified progress/busy presentation**: A root scan (`MainWindowViewModel`) and a chart-initiated scoped scan (`ChartViewModel`, drill-down/"Go up"/stale-root rescan) both drive the same toolbar progress bar and Cancel button; `MainWindowViewModel.DisplayProgressValue`/`DisplayProgressIsIndeterminate`/`IsBusy` pick whichever scan is active. A directory scan without a known total (`ScanProgress.PercentComplete == null`) is indeterminate; it becomes determinate once a percentage is reported. `ChartView` always overlays a dimming, pointer- and keyboard-blocking `BusySpinnerControl` over the chart during any root or scoped scan (`ChartViewModel.IsChartScanning`), disabling `SunburstChartControl` via `IsEnabled` rather than only covering it.
- **Bounds**: `ScanTreeBudget.Default` retains at most 100,000 nodes, 99 children per retained directory, six retained levels, and 10,000 inaccessible path samples.
- **Parallelism**: directories fan their children out across a scan-wide slot budget for the first `ScanTreeBudget.ParallelFanOutLevels` levels — default `1` (root only); deeper subtrees walk their whole tree sequentially on one shared slot. `2` and `3` remain explicit, measured knobs, not defaults. `MaxDegreeOfParallelism` (default `0`, resolving to `Math.Min(Environment.ProcessorCount, 16)`) caps concurrent native reads and outstanding 1 MiB buffer rentals, not open-cursor count — a parent cursor can stay open across an awaited child. Fan-out only happens when `VolumeParallelismPolicy` detects no seek penalty (SSD/NVMe); HDDs and unknown volumes stay sequential.
- **Reparse points**: skip them unless `FILE_ATTRIBUTE_OFFLINE` is set. Preserve this behavior unless the change explicitly adds safer tag/identity/cycle validation.
- **Denied directories**: `FsItem.Items == null` means open failed; an empty list means the directory opened but retained no children. Inaccessible paths are sampled and may be truncated.

## Build & run

Requires the .NET SDK baseline `10.0.100` from `global.json` (`latestFeature` roll-forward).

```powershell
dotnet restore SizeScanner.slnx
dotnet build SizeScanner.slnx -c Debug
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release
dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release
dotnet test SizeScanner.slnx -c Release
dotnet run --project .\SizeScanner.Avalonia\SizeScanner.Avalonia.csproj

# Scanner harness (path argument optional):
dotnet run --project .\ScannerConsole\ScannerConsole.csproj -- C:\some\folder

# Release-like publish (single-file, trimmed, native AoT):
dotnet publish .\SizeScanner.Avalonia\SizeScanner.Avalonia.csproj -c Release -r win-x64
```

Opt-in performance tests require `SIZESCANNER_RUN_PERF_TESTS=1`. Never run destructive filesystem tests against user data; use the test projects' temporary-directory fixtures.

**Key NuGet packages** (versions in `Directory.Packages.props`):
- `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` — Avalonia UI
- `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection` — MVVM and DI
- `Spectre.Console` — console harness output only
- `MinVer` — Git tag-based assembly versioning (`v*` tags, `MinVerTagPrefix=v`)
- `xunit.v3`, `Microsoft.NET.Test.Sdk`, `coverlet.collector` — test projects

## CI and release

- `.github/workflows/dotnet-desktop.yml` tests both projects with coverage and builds the solution in Debug and Release on pushes and pull requests to `main`/`master`.
- `.github/workflows/codeql.yml` runs C# CodeQL analysis.
- `.github/workflows/release.yml` publishes a self-contained Native AOT `win-x64` build and creates `SizeScanner-win-x64.zip` for `v*` tags.
- The release workflow does not run tests independently, and pull-request CI does not run a Native AOT publish. Verify both explicitly when changing trimming, serialization, interop, or packaging.
- No canonical `.gitlab-ci.yml` is present.

## Conventions when editing

- Keep P/Invoke and native structs in `ScannerCore` (`DirectoryScanner` enumeration, `VolumeParallelismPolicy` seek-penalty detection) — never call Win32 from the UI.
- Avalonia UI lives in `SizeScanner.Avalonia/`; keep platform/IO behind interfaces in `Abstractions/` with Windows implementations in `Services/`. Chart building belongs in `Charting/SunburstChartBuilder.cs`; view-models in `ViewModels/`.
- Add new adapters by defining a narrow interface in `Abstractions/`, implementing it in `Services/`, registering it in `App.axaml.cs`, and testing through a manual fake.
- Settings persist to `%AppData%\SizeScanner\settings.avalonia.json` via `JsonSettingsStore` and `Models/UserSettings.cs`.
- The application must stay **Native AOT and trimming compatible** (`PublishAot`, `PublishTrimmed`, `IsAotCompatible`). Use compiled AXAML bindings, explicit DI registration, and source-generated JSON metadata; avoid reflection/dynamic activation unless explicitly annotated and publish-tested.
- Synthetic drive entries: use `DriveScanMetadata` constants/helpers; chart-only synthetic names and UI rules belong in `ChartDisplayMetadata` / `ChartNodeRules`. Do not hard-code `[Free space]`, `[Inaccessible]`, `[Filtered]`, `[Other]`, or synthetic indices.
- Filter threshold: use `FilterThreshold.PercentFromIndex()` and `FilterThreshold.GetDisplayTotal()`; do not duplicate the `0.0025f × FilterIndex` factor.
- Settings are loaded into `MainWindowViewModel` once and persisted through its in-memory `UserSettings` snapshot; views should not independently load settings.
- `ChartViewModel` owns scope, hover, context target, and delete commands; views should delegate policy decisions such as context-menu suppression to the VM/chart rules.
- Root and scoped scan failures are recoverable UI outcomes: show them through the injected `IDialogService`, retain the previous chart, and restore transient busy/progress state from `finally`.
- Preserve bounded-tree semantics: exact totals do not imply a complete retained tree. Keep aggregate identity, parent links, `HasUnretainedChildren`, and root-vs-scope allocation-size behavior intact.
- Preserve cancellation as control flow. Do not return a partial tree after cancellation or worker failure, and do not race root scans against the singleton `ScanService`.
- Treat native parsing, root-level channel coordination, reparse handling, and delete/scan concurrency as fragile areas. Add focused tests for malformed records, worker faults, cancellation, identity edge cases, or operation overlap when touching them.
- Follow `.editorconfig`: UTF-8, final newline, four-space C# indentation, System-first imports, nullable enabled, and explicit usings. New C# files need the copyright/SPDX header and should normally use file-scoped namespaces.
- Tests use xUnit's built-in `Assert`, manual fakes/synthetic sources, and arrange/act/assert. Name files/classes `<Subject>Tests` and methods `Subject_or_scenario_expected_behavior`.
- After adding production code or completing a refactor, update `AGENTS.md` and every affected document under `.planning/codebase/` in the same change. Keep architecture, structure, stack, conventions, testing, integrations, concerns, dates, and repository-relative paths aligned with the resulting code.

## Key files

- `ScannerCore/DirectoryScanner.cs` — symlink/offline handling, native enumeration
- `ScannerCore/DirectoryEntryCursor.cs` — native cursor/source/sink test seam
- `ScannerCore/BoundedDirectoryWalker.cs` — exact-total traversal and bounded depth-limited fan-out
- `ScannerCore/BoundedChildCollector.cs` — largest-child retention and aggregate creation
- `ScannerCore/ScanTreeBudget.cs` — retention, inaccessible-path, fan-out-depth, and shared parallelism limits
- `ScannerCore/DirectoryWalkEngine.cs` — scan sizing and SSD-gated depth-limited fan-out
- `ScannerCore/VolumeParallelismPolicy.cs` — P/Invoke seek-penalty detection gating parallelism
- `ScannerCore/DriveScanner.cs` — scan orchestration, progress, inaccessible tracking
- `ScannerCore/DriveScanMetadata.cs` — synthetic drive scan entry names/accessors/insertion
- `ScannerCore/ScanProgress.cs` — progress report record for `IProgress`
- `ScannerCore/FsItem.cs` — tree node (`Items` null = access denied dir)
- `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs` — capped sunburst segment building
- `SizeScanner.Avalonia/Charting/ChartNodeRules.cs` — chart synthetic/scoping/context-menu rules
- `SizeScanner.Avalonia/Charting/SunburstHitTest.cs` / `SunburstChart.cs` — per-ring segment indexes and hit-testing
- `SizeScanner.Avalonia/Views/BusySpinnerControl.cs` — dependency-free circular busy spinner; `DispatcherTimer` runs only while attached to the visual tree and active
- `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` — toolbar, scan orchestration, unified progress presentation, settings
- `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` — chart scope, hover, context actions, scoped scan progress, unified chart busy state
- `SizeScanner.Avalonia/Models/UserSettings.cs` — persisted settings DTO
- `SizeScanner.Avalonia/Services/JsonSettingsStore.cs` — JSON settings load/save
- `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs` — AOT-safe JSON metadata
- `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs` — Explorer and delete operations

## Reference docs

Use the current path-oriented maps under `.planning/codebase/` before changing a subsystem:

- `ARCHITECTURE.md` — boundaries, data flows, state ownership, constraints, and anti-patterns
- `STRUCTURE.md` — directory purposes, key locations, and where new code belongs
- `STACK.md` — runtime, dependencies, build configuration, and platform requirements
- `CONVENTIONS.md` — naming, formatting, error handling, logging, and module design
- `TESTING.md` — test organization, fakes/fixtures, coverage, performance gates, and CI practices
- `INTEGRATIONS.md` — local Windows APIs, persistence, elevation, and GitHub automation
- `CONCERNS.md` — known bugs, security risks, scaling limits, fragile areas, and coverage gaps
