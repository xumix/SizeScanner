<!-- refreshed: 2026-07-16 -->
# Architecture

**Analysis Date:** 2026-07-16

## System Overview

```text
┌──────────────────────────────────────────────────────────────────────────┐
│                         Process Entry Points                             │
├───────────────────────────────────┬──────────────────────────────────────┤
│ Avalonia desktop application      │ Development console harness          │
│ `SizeScanner.Avalonia/Program.cs` │ `ScannerConsole/Program.cs`          │
└─────────────────┬─────────────────┴──────────────────┬───────────────────┘
                  │                                    │
                  ▼                                    │
┌──────────────────────────────────────────────────────┴───────────────────┐
│                     Application / Presentation Layer                    │
│ `SizeScanner.Avalonia/ViewModels/` · `Views/` · `Charting/`             │
│ `SizeScanner.Avalonia/Abstractions/` · `Services/`                      │
└─────────────────────────────────────┬────────────────────────────────────┘
                                      │ project reference
                                      ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                         Filesystem Scan Core                             │
│ `ScannerCore/DriveScanner.cs` → selector → walk engine → native scanner │
│ Tree and progress contracts: `ScannerCore/FsItem.cs`, `ScanProgress.cs` │
└─────────────────────────────────────┬────────────────────────────────────┘
                                      │ Win32 / NT native calls
                                      ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ Windows filesystem and storage APIs                                     │
│ `kernel32.dll` · `ntdll.dll` · local filesystem · `%AppData%`           │
└──────────────────────────────────────────────────────────────────────────┘
```

The solution is a Windows-only layered desktop application. `ScannerCore/` owns scan semantics and native enumeration, while `SizeScanner.Avalonia/` owns composition, user interaction, chart transformation, and rendering. `ScannerConsole/` is an alternate manual entry point over the same scan core. Both test projects depend inward on production projects, as declared in `SizeScanner.slnx`.

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| Desktop bootstrap | Starts Avalonia on an STA thread and configures the desktop lifetime | `SizeScanner.Avalonia/Program.cs` |
| Composition root | Registers service, view-model, and window singletons and resolves the main window | `SizeScanner.Avalonia/App.axaml.cs` |
| Main orchestration VM | Owns drive discovery, scan lifecycle, cancellation, progress, display options, inaccessible paths, and settings snapshot | `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` |
| Chart interaction VM | Owns chart scope, hover state, context targets, deletion actions, and layout rebuilds | `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` |
| UI service boundary | Defines mockable contracts for scanning, settings, dialogs, drives, folder selection, elevation, and filesystem actions | `SizeScanner.Avalonia/Abstractions/` |
| UI service implementations | Adapts Avalonia and Windows facilities to the abstraction layer | `SizeScanner.Avalonia/Services/` |
| Scan facade | Chooses drive versus directory semantics, tracks totals and inaccessible paths, and throttles progress reports | `ScannerCore/DriveScanner.cs` |
| Engine selection | Selects the first capable scan engine and permits fallback after non-cancellation failures | `ScannerCore/ScanEngineSelector.cs` |
| Directory walk engine | Recursively builds the tree and gates top-level parallel traversal by volume type | `ScannerCore/DirectoryWalkEngine.cs` |
| Native enumerator | Enumerates one directory with `NtQueryDirectoryFile` and converts records to `FsItem` children | `ScannerCore/DirectoryScanner.cs` |
| Scan domain tree | Represents directories, files, parent links, sizes, and inaccessible-directory state | `ScannerCore/FsItem.cs` |
| Chart transformation | Converts an `FsItem` subtree into bounded, filtered, colored sunburst segments | `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs` |
| Chart rendering | Caches Avalonia geometries and draws the sunburst and hover outline | `SizeScanner.Avalonia/Views/SunburstChartControl.cs` |
| Console harness | Exercises directory scanning and reports elapsed time and inaccessible paths | `ScannerConsole/Program.cs` |

## Pattern Overview

**Overall:** Layered MVVM application with ports/adapters around UI and platform operations, plus a reusable scan-engine strategy.

**Key Characteristics:**
- Dependency direction is presentation and harness → `ScannerCore/`; `ScannerCore/ScannerCore.csproj` has no project or package dependencies.
- `SizeScanner.Avalonia/App.axaml.cs` is the composition root; constructor injection supplies interfaces to view-models and UI-facing services.
- `MainWindowViewModel` orchestrates application state, while `ChartViewModel` isolates visualization scope and context-action policy in `SizeScanner.Avalonia/ViewModels/`.
- Scan implementations conform to `IScanEngine` in `ScannerCore/IScanEngine.cs`; `ScanEngineSelector` provides an ordered strategy/fallback seam.
- The scan result is a mutable `FsItem` tree shared from scanning through charting; chart segments retain source-node references through `SizeScanner.Avalonia/Charting/SunburstSegment.cs`.
- Views use compiled Avalonia bindings from `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`; code-behind handles lifecycle and pointer mechanics, while view-models own policy and commands.

## Layers

**Executable Bootstrap:**
- Purpose: Enter the process, initialize the UI framework, and build the dependency graph.
- Location: `SizeScanner.Avalonia/Program.cs`, `SizeScanner.Avalonia/App.axaml.cs`, `ScannerConsole/Program.cs`
- Contains: STA desktop startup, service registration, main-window resolution, and console harness startup.
- Depends on: Avalonia desktop lifetime in `SizeScanner.Avalonia/Program.cs`; `ScannerCore` and Spectre.Console in `ScannerConsole/Program.cs`.
- Used by: The operating system or `dotnet run`.

**Presentation and Application Orchestration:**
- Purpose: Translate user actions into scan, chart, navigation, delete, and persistence operations.
- Location: `SizeScanner.Avalonia/Views/`, `SizeScanner.Avalonia/ViewModels/`
- Contains: XAML views, minimal code-behind, CommunityToolkit-generated commands/properties, and custom rendering.
- Depends on: Contracts in `SizeScanner.Avalonia/Abstractions/`, chart types in `SizeScanner.Avalonia/Charting/`, and domain types in `ScannerCore/`.
- Used by: The Avalonia application lifetime configured in `SizeScanner.Avalonia/App.axaml.cs`.

**UI Platform Adapters:**
- Purpose: Hide concrete Avalonia and Windows operations behind testable interfaces.
- Location: `SizeScanner.Avalonia/Abstractions/`, `SizeScanner.Avalonia/Services/`
- Contains: `IScanService`, `ISettingsStore`, `IFileSystemActions`, `IDialogService`, `IFolderPicker`, `IDriveProvider`, `IElevationService`, and `ITopLevelProvider` plus implementations.
- Depends on: Avalonia APIs, Windows process/filesystem APIs, JSON source generation, and `ScannerCore`.
- Used by: `MainWindowViewModel`, `ChartViewModel`, and other service adapters.

**Chart Domain and Layout:**
- Purpose: Apply display policy to a scan tree and create renderable geometry metadata.
- Location: `SizeScanner.Avalonia/Charting/`
- Contains: threshold calculation, synthetic-node rules, segment construction, color selection, ring layout, indexed hit testing, and tooltip formatting.
- Depends on: `ScannerCore/FsItem.cs` and Avalonia primitive types such as `Color`, `Point`, and `Size`.
- Used by: `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, `SizeScanner.Avalonia/Views/ChartView.axaml.cs`, and `SizeScanner.Avalonia/Views/SunburstChartControl.cs`.

**Scan Orchestration and Domain:**
- Purpose: Define stable scan semantics and produce an `FsItem` tree independently of the UI.
- Location: `ScannerCore/`
- Contains: facade, engine contract and selector, directory walk, native enumeration, storage policy, progress DTO, synthetic drive metadata, path helpers, and humanized display formatting.
- Depends on: .NET base libraries and Windows native APIs only.
- Used by: `SizeScanner.Avalonia/Services/ScanService.cs`, `ScannerConsole/Program.cs`, and `ScannerCore.Tests/`.

**Verification:**
- Purpose: Exercise scan-core behavior and Avalonia chart/service/view-model behavior separately.
- Location: `ScannerCore.Tests/`, `SizeScanner.Avalonia.Tests/`
- Contains: xUnit tests, temporary-directory helpers, test trees, fakes, smoke tests, and platform integration tests.
- Depends on: Production projects through references in `ScannerCore.Tests/ScannerCore.Tests.csproj` and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.
- Used by: Local `dotnet test` commands and CI workflows under `.github/workflows/` and `.gitlab-ci.yml`.

## Data Flow

### Desktop Startup

1. `Program.Main` enters on an STA thread and starts the classic desktop lifetime (`SizeScanner.Avalonia/Program.cs:11`).
2. Avalonia loads application XAML and calls `App.OnFrameworkInitializationCompleted` (`SizeScanner.Avalonia/App.axaml.cs:19`).
3. `App.ConfigureServices` registers singleton adapters, view-models, and `MainWindow` (`SizeScanner.Avalonia/App.axaml.cs:32`).
4. Dependency injection constructs `MainWindow`; its code-behind assigns `MainWindowViewModel` as `DataContext` (`SizeScanner.Avalonia/Views/MainWindow.axaml.cs:14`).
5. On window open, `TopLevelProvider` is registered and `MainWindowViewModel.Initialize` loads ready drives and applies the already-loaded settings snapshot (`SizeScanner.Avalonia/Views/MainWindow.axaml.cs:22`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:109`).

### Primary Scan Path

1. A drive button, Browse command, or Rescan command reaches `MainWindowViewModel.ScanTargetAsync` (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:165`).
2. The view-model creates a new cancellation source and a UI-context `Progress<ScanProgress>`, then calls `IScanService.RunAsync` (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:168`).
3. `ScanService` records rescan state, constructs a fresh `DriveScanner`, and moves synchronous scanning to a thread-pool task (`SizeScanner.Avalonia/Services/ScanService.cs:15`).
4. `DriveScanner` selects drive or directory behavior and delegates to its configured `IScanEngine` (`ScannerCore/DriveScanner.cs:54`, `ScannerCore/DriveScanner.cs:66`).
5. `ScanEngineSelector` evaluates engines in order; the default `DriveScanner` currently registers `DirectoryWalkEngine` as the always-capable fallback (`ScannerCore/DriveScanner.cs:24`, `ScannerCore/ScanEngineSelector.cs:29`).
6. `DirectoryWalkEngine` creates the root `FsItem`, determines whether the target volume is SSD-class, scans each directory, and recurses through child directories (`ScannerCore/DirectoryWalkEngine.cs:27`).
7. `DirectoryScanner` opens each directory with `CreateFile`, enumerates records via `NtQueryDirectoryFile`, skips reparse points unless offline, and emits child `FsItem` objects (`ScannerCore/DirectoryScanner.cs:82`, `ScannerCore/DirectoryScanner.cs:138`).
8. The walk attaches parent pointers, sums child sizes into directories, marks denied directories with `Items == null`, and records their paths (`ScannerCore/DirectoryWalkEngine.cs:99`, `ScannerCore/FsItem.cs:27`).
9. A drive scan prepends `[Free space]` and `[Inaccessible]` nodes through `DriveScanMetadata`; a directory scan returns the raw root (`ScannerCore/DriveScanner.cs:54`, `ScannerCore/DriveScanMetadata.cs:20`).
10. `MainWindowViewModel` populates inaccessible-path state, gives the tree to `ChartViewModel`, rebuilds the chart, and restores idle UI state (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:190`).

### Progress and Cancellation Flow

1. `DirectoryWalkEngine.WalkContext` maintains the scanned-byte total with `Interlocked` and reports the current directory before enumeration (`ScannerCore/DirectoryWalkEngine.cs:132`).
2. `DriveScanner.OnEngineProgress` updates public scan state under `_progressLock` and throttles non-final callbacks to 300 ms (`ScannerCore/DriveScanner.cs:86`).
3. `Progress<ScanProgress>` posts updates to the captured UI synchronization context, where `MainWindowViewModel.OnScanProgress` updates the status and percentage (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:220`).
4. Cancellation is requested by `CancelScanCommand`, observed by the walk and parallel-loop token, and normalized to a cancelled UI state (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:149`, `ScannerCore/DirectoryWalkEngine.cs:37`).

### Scan Tree to Sunburst Rendering

1. `ChartViewModel.SetScan` retains the real scan root and creates a shallow drive-root clone used only to hide `[Free space]` while retaining `[Inaccessible]` (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:49`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:261`).
2. `MainWindowViewModel.RefreshChart` converts the selected filter index through `FilterThreshold.PercentFromIndex` and passes free-space visibility to `ChartViewModel` (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:248`).
3. `ChartViewModel.RebuildLayout` selects the scoped or base root, computes the byte threshold, and calls `SunburstChartBuilder.Build` (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:65`).
4. `SunburstChartBuilder` computes visible sizes, aggregates filtered items and segment overflow, enforces global/per-sector caps, and returns an immutable-facing `SunburstChart` (`SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs:35`).
5. Compiled binding passes `ChartViewModel.Layout` to `SunburstChartControl.Chart` (`SizeScanner.Avalonia/Views/ChartView.axaml:21`).
6. `SunburstChartControl` caches geometry per chart/bounds pair and draws segments, hover outline, and center label (`SizeScanner.Avalonia/Views/SunburstChartControl.cs:98`).

### Pointer Interaction and Context Actions

1. `ChartView` receives pointer events and delegates radial hit testing to `SunburstChartControl` (`SizeScanner.Avalonia/Views/ChartView.axaml.cs:66`).
2. `SunburstHitTest` resolves a ring, binary-searches that ring's lazily built actionable-segment index, and returns the source-bearing segment (`SizeScanner.Avalonia/Charting/SunburstHitTest.cs:10`, `SizeScanner.Avalonia/Charting/SunburstChart.cs:21`).
3. `ChartViewModel` owns hover text, scope transitions, synthetic-node suppression, and context-target paths (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:81`).
4. Delete commands confirm through `IDialogService`, execute through `IFileSystemActions`, remove the node from the in-memory tree, decrement ancestor sizes, and rebuild the layout (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:163`).

### Settings Persistence

1. `MainWindowViewModel` loads one mutable `UserSettings` snapshot in its constructor through `ISettingsStore` (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:36`).
2. Display-option and pane changes mutate that snapshot and save it; window dimensions and splitter distance are captured on close (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:266`, `SizeScanner.Avalonia/Views/MainWindow.axaml.cs:30`).
3. `JsonSettingsStore` persists source-generated JSON at `%AppData%\SizeScanner\settings.avalonia.json` and falls back to defaults on load failure (`SizeScanner.Avalonia/Services/JsonSettingsStore.cs:14`, `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs:8`).

**State Management:**
- Application state lives in singleton `MainWindowViewModel` and `ChartViewModel` instances registered by `SizeScanner.Avalonia/App.axaml.cs`.
- Scan state is per-run: `ScanService` replaces its `DriveScanner` for each call, while `MainWindowViewModel` replaces its `CancellationTokenSource` and root.
- The `FsItem` tree is intentionally mutable so deletion can update parents and sizes in place in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- Render caches are control-instance state in `SizeScanner.Avalonia/Views/SunburstChartControl.cs`; the chart model lazily caches per-ring indexes in `SizeScanner.Avalonia/Charting/SunburstChart.cs`.

## Key Abstractions

**`IScanEngine`:**
- Purpose: Define equivalent tree-building behavior for interchangeable scan engines.
- Examples: `ScannerCore/IScanEngine.cs`, `ScannerCore/DirectoryWalkEngine.cs`, `ScannerCore/ScanEngineSelector.cs`
- Pattern: Strategy plus ordered chain-of-responsibility fallback.

**`IScanService`:**
- Purpose: Bridge asynchronous UI orchestration to synchronous scan-core execution and expose rescan metadata.
- Examples: `SizeScanner.Avalonia/Abstractions/IScanService.cs`, `SizeScanner.Avalonia/Services/ScanService.cs`
- Pattern: Application service adapter.

**UI Service Interfaces:**
- Purpose: Keep view-model policy independent from concrete dialogs, pickers, elevation, drive discovery, settings storage, and filesystem actions.
- Examples: `SizeScanner.Avalonia/Abstractions/`, `SizeScanner.Avalonia/Services/`
- Pattern: Ports and adapters with constructor injection.

**`FsItem`:**
- Purpose: Carry scan hierarchy, aggregate sizes, access-denied state, and parent-based path reconstruction.
- Examples: `ScannerCore/FsItem.cs`, `ScannerCore/DriveScanMetadata.cs`
- Pattern: Mutable composite tree with parent links.

**`SunburstChart`:**
- Purpose: Hold bounded render segments and provide lazy per-ring indexes for efficient interaction.
- Examples: `SizeScanner.Avalonia/Charting/SunburstChart.cs`, `SizeScanner.Avalonia/Charting/SunburstSegment.cs`
- Pattern: Presentation model with internal query cache.

**Synthetic Metadata Rules:**
- Purpose: Give drive-scan and chart-only synthetic nodes stable names, indices, visibility, scoping, and context-menu semantics.
- Examples: `ScannerCore/DriveScanMetadata.cs`, `SizeScanner.Avalonia/Charting/ChartDisplayMetadata.cs`, `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`
- Pattern: Centralized metadata and policy helpers.

## Entry Points

**Avalonia Desktop:**
- Location: `SizeScanner.Avalonia/Program.cs`
- Triggers: Windows process startup or `dotnet run --project SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`.
- Responsibilities: Establish STA execution, configure Avalonia platform detection, Inter font, trace logging, and classic desktop lifetime.

**Avalonia Composition:**
- Location: `SizeScanner.Avalonia/App.axaml.cs`
- Triggers: Avalonia framework initialization.
- Responsibilities: Load application resources, configure dependency injection, and construct the main window.

**Console Scan Harness:**
- Location: `ScannerConsole/Program.cs`
- Triggers: `dotnet run --project ScannerConsole/ScannerConsole.csproj -- <path>`.
- Responsibilities: Validate directory scanning outside the UI and display duration, total size, and inaccessible paths.

**Public Scan API:**
- Location: `ScannerCore/DriveScanner.cs`
- Triggers: Calls to `ScanDrive` or `ScanDirectory` from UI, console, or tests.
- Responsibilities: Normalize scan setup, apply drive-specific metadata, expose progress, and retain inaccessible paths.

## Architectural Constraints

- **Platform:** Every project targets `net10.0-windows`; production scanning requires `kernel32.dll` and `ntdll.dll` through `ScannerCore/DirectoryScanner.cs` and `ScannerCore/VolumeParallelismPolicy.cs`.
- **Dependency direction:** Keep filesystem scan logic in `ScannerCore/`; `ScannerCore/ScannerCore.csproj` must not reference the Avalonia project.
- **Size semantics:** Drive scans use allocation size and directory scans use logical end-of-file size; the flag flows from `DriveScanner` through `DirectoryWalkEngine` into `DirectoryScanner`.
- **Reparse semantics:** Skip reparse points unless `FILE_ATTRIBUTE_OFFLINE`; preserve this contract for every implementation of `IScanEngine` in `ScannerCore/IScanEngine.cs`.
- **Access-denied semantics:** Represent an inaccessible directory as `FsItem.Items == null` and also add its path to `ScanResult.Inaccessible`.
- **Threading:** UI commands start on the Avalonia thread; `ScanService` uses `Task.Run`; only directory subtrees are parallelized, and only when `VolumeParallelismPolicy` reports no seek penalty.
- **Progress:** `DriveScanner` serializes updates with `_progressLock` and throttles callbacks; consumers should use `IProgress<ScanProgress>` rather than reading scan fields as a polling protocol.
- **AOT/trimming:** The UI project enables native AOT, trimming, and compiled bindings in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`; serialization uses `SizeScannerJsonContext`.
- **Global state:** DI registrations are singleton-scoped in `SizeScanner.Avalonia/App.axaml.cs`; no mutable process-wide static scan state is present.
- **Circular dependencies:** Project references are acyclic: tests/UI/console point toward `ScannerCore`; `ScannerCore` points only to framework libraries.
- **Chart capacity:** `SunburstChartBuilder` caps output at 100,000 total segments and 100 per sector in `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`.

## Anti-Patterns

### Filesystem Enumeration in the UI Layer

**What happens:** Adding scan traversal or native enumeration under `SizeScanner.Avalonia/` bypasses the reusable scan contract.
**Why it's wrong:** It reverses the project dependency boundary, makes console/test behavior diverge, and risks changing drive-versus-directory size semantics.
**Do this instead:** Add filesystem scan behavior under `ScannerCore/` and expose it through `ScannerCore/IScanEngine.cs` or `ScannerCore/DriveScanner.cs`.

### Duplicated Synthetic Names or Threshold Math

**What happens:** Hard-coded `[Free space]`, `[Inaccessible]`, `[Filtered]`, `[Other]`, or `0.0025 × index` logic can drift between tree, chart, and interaction code.
**Why it's wrong:** Node identity controls visibility, coloring, scoping, and destructive-action suppression.
**Do this instead:** Use `ScannerCore/DriveScanMetadata.cs`, `SizeScanner.Avalonia/Charting/ChartDisplayMetadata.cs`, `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`, and `SizeScanner.Avalonia/Charting/FilterThreshold.cs`.

### View-Owned Interaction Policy

**What happens:** Deciding whether a node may scope, show a context menu, or be deleted directly in `ChartView.axaml.cs` duplicates domain/UI policy.
**Why it's wrong:** Pointer mechanics become inseparable from policy and view-model tests cannot cover the behavior.
**Do this instead:** Keep hit-event wiring in `SizeScanner.Avalonia/Views/ChartView.axaml.cs` and delegate decisions to `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` and `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`.

### Reflection-Based Activation or Serialization

**What happens:** Runtime type discovery, unannotated reflection, or default reflection serialization may be removed by trimming or fail under native AOT.
**Why it's wrong:** The production publish contract in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj` enables both trimming and native AOT.
**Do this instead:** Use explicit DI registration in `SizeScanner.Avalonia/App.axaml.cs`, compiled XAML bindings, and source-generated JSON metadata in `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs`.

## Error Handling

**Strategy:** Convert expected boundary failures into safe result/state values, preserve cancellation, and permit engine fallback for implementation failures.

**Patterns:**
- `ScanEngineSelector` rethrows `OperationCanceledException`, logs other engine failures to `Debug`, and tries the next capable engine in `ScannerCore/ScanEngineSelector.cs`.
- Native directory open failure returns `null`; `DirectoryWalkEngine` maps that to `Items == null` and records the path in `ScannerCore/DirectoryWalkEngine.cs`.
- Unknown storage characteristics fail closed to sequential traversal in `ScannerCore/VolumeParallelismPolicy.cs`.
- Settings load failure returns a default `UserSettings` in `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`.
- Delete failures become `DeleteResult` values and are displayed through `IDialogService` in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- UI cancellation is handled separately from scan failure in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`.

## Cross-Cutting Concerns

**Logging:** Avalonia routes framework logs to trace in `SizeScanner.Avalonia/Program.cs`; native enumeration and engine fallback write diagnostics through `System.Diagnostics.Debug` in `ScannerCore/DirectoryScanner.cs` and `ScannerCore/ScanEngineSelector.cs`.

**Validation:** Target existence is explicitly checked by `ScannerConsole/Program.cs`; UI target selection is constrained by `DriveProvider` and `AvaloniaFolderPicker`; filesystem and native failures are handled at service/engine boundaries.

**Authentication:** Not applicable. Elevation is optional Windows administrator relaunch behavior through `SizeScanner.Avalonia/Abstractions/IElevationService.cs` and `SizeScanner.Avalonia/Services/WindowsElevationService.cs`.

**Configuration:** Shared compilation defaults are in `Directory.Build.props`; package versions are centralized in `Directory.Packages.props`; the SDK is pinned by `global.json`.

---

*Architecture analysis: 2026-07-16*
