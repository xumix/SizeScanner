<!-- refreshed: 2026-08-06 -->
# Architecture

**Analysis Date:** 2026-08-06

## System Overview

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│                     Windows Desktop Presentation                            │
├──────────────────────┬────────────────────────┬──────────────────────────────┤
│ Avalonia views/XAML  │ MVVM orchestration     │ Custom chart rendering       │
│ `SizeScanner.        │ `SizeScanner.          │ `SizeScanner.Avalonia/        │
│ Avalonia/Views/`     │ Avalonia/ViewModels/`  │ Charting/` + `Views/          │
│                      │                        │ SunburstChartControl.cs`       │
└──────────┬───────────┴────────────┬───────────┴──────────────┬───────────────┘
           │ bindings/commands      │ interfaces               │ FsItem input
           ▼                        ▼                          ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                  Application Services and Adapters                           │
│ `SizeScanner.Avalonia/Abstractions/` + `SizeScanner.Avalonia/Services/`      │
│ scan scheduling, settings, dialogs, drive discovery, elevation, file actions│
└──────────────────────────────┬───────────────────────────────────────────────┘
                               │ project reference / `IScanService`
                               ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                  Filesystem Scan Domain and Engine                           │
│ `ScannerCore/`                                                               │
│ scan facade → engine selection → native cursor → bounded directory walker    │
│ → exact totals plus a bounded `FsItem` snapshot                              │
└───────────────┬──────────────────────────┬───────────────────────────────────┘
                │                          │
                ▼                          ▼
┌────────────────────────────┐  ┌──────────────────────────────────────────────┐
│ Windows filesystem APIs    │  │ Local application state / side effects       │
│ `kernel32.dll`, `ntdll.dll`│  │ `%AppData%\SizeScanner\settings.avalonia.json`│
│ NTFS/volume metadata       │  │ Explorer, recycle bin, permanent deletion    │
└────────────────────────────┘  └──────────────────────────────────────────────┘

         `ScannerConsole/` ───────────────► `ScannerCore/`
         test projects ──────────────────► their production project(s)
```

The solution is an acyclic set of five projects declared in `SizeScanner.slnx`.
`ScannerCore/` owns the filesystem model and all native scanning. The Avalonia
application references that library and divides desktop behavior into views,
view-models, charting code, service contracts, and Windows/Avalonia service
implementations. `ScannerConsole/` is a manual scan/performance entry point, not
part of the production UI.

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| Desktop bootstrap | Starts the STA Avalonia desktop lifetime | `SizeScanner.Avalonia/Program.cs` |
| Composition root | Registers singleton application services, view-models, and the main window | `SizeScanner.Avalonia/App.axaml.cs` |
| Main window | Hosts toolbar, chart, progress/status, and inaccessible-path pane | `SizeScanner.Avalonia/Views/MainWindow.axaml` |
| Main orchestration VM | Owns root scan commands, cancellation, progress, settings snapshot, drives, and inaccessible-path display | `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` |
| Chart interaction VM | Owns chart scope, layout, hover/context state, scoped rescans, and deletion workflows | `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` |
| Scan adapter | Moves synchronous core scans off the UI thread and distinguishes root state from throwaway scope scans | `SizeScanner.Avalonia/Services/ScanService.cs` |
| Chart builder | Converts an `FsItem` tree into capped, filtered, ring-indexed sunburst segments | `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs` |
| Chart model/hit testing | Stores immutable segment layout and builds per-ring indexes for polar hit testing | `SizeScanner.Avalonia/Charting/SunburstChart.cs`, `SizeScanner.Avalonia/Charting/SunburstHitTest.cs` |
| Chart control | Caches Avalonia geometries and brushes, renders segments, hover outline, and center total | `SizeScanner.Avalonia/Views/SunburstChartControl.cs` |
| Scan facade | Normalizes drive/directory scans, progress, inaccessible metadata, and synthetic drive entries | `ScannerCore/DriveScanner.cs` |
| Engine strategy | Defines scan-engine equivalence and ordered fallback selection | `ScannerCore/IScanEngine.cs`, `ScannerCore/ScanEngineSelector.cs` |
| Directory engine | Chooses allocation/logical sizing and SSD-gated depth-limited fan-out | `ScannerCore/DirectoryWalkEngine.cs` |
| Native enumeration | Streams Windows directory records through a cursor/sink protocol | `ScannerCore/DirectoryScanner.cs`, `ScannerCore/DirectoryEntryCursor.cs` |
| Bounded tree walk | Computes exact totals while retaining a depth/width/node-bounded snapshot | `ScannerCore/BoundedDirectoryWalker.cs`, `ScannerCore/BoundedChildCollector.cs` |
| Scan domain model | Represents files, directories, aggregates, parent links, denied directories, and hidden descendants | `ScannerCore/FsItem.cs` |
| Settings persistence | Loads/saves the AOT-safe JSON settings DTO under the current user's application data | `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`, `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs` |
| Windows actions | Provides Explorer selection, recycle/permanent deletion, drive enumeration, and UAC relaunch behind interfaces | `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`, `SizeScanner.Avalonia/Services/DriveProvider.cs`, `SizeScanner.Avalonia/Services/WindowsElevationService.cs` |

## Pattern Overview

**Overall:** Layered MVVM desktop application with ports/adapters at the UI
boundary and a strategy-based, streaming scan engine in a separate core library.

**Key Characteristics:**
- Keep the project dependency graph one-way: UI, console, and tests depend on
  `ScannerCore/`; `ScannerCore/` never depends on Avalonia.
- Use constructor-injected contracts from `SizeScanner.Avalonia/Abstractions/`
  to isolate view-models from dialogs, storage pickers, drives, elevation, file
  mutation, settings persistence, and scan scheduling.
- Keep Windows native directory/volume calls in `ScannerCore/DirectoryScanner.cs`
  and `ScannerCore/VolumeParallelismPolicy.cs`.
- Treat `FsItem` as the shared boundary model between scanning, chart building,
  interaction, and tests.
- Build a bounded retained snapshot, not a complete in-memory filesystem tree:
  `ScannerCore/ScanTreeBudget.cs` limits retained nodes, children, depth,
  inaccessible paths, fan-out depth, and shared parallelism degree.
- Re-scan a selected directory for drill-down detail rather than retaining
  unbounded navigation history; `ChartViewModel` owns at most the root tree and
  current scope tree.
- Keep chart layout independent from the Avalonia control. Charting files produce
  records and geometry inputs; `SunburstChartControl` performs actual rendering.
- Preserve native AOT and trimming compatibility through compiled XAML bindings,
  explicit DI registration, and generated JSON metadata.

## Layers

**Desktop Host and Composition:**
- Purpose: Start Avalonia and assemble the concrete object graph.
- Location: `SizeScanner.Avalonia/Program.cs`, `SizeScanner.Avalonia/App.axaml`,
  `SizeScanner.Avalonia/App.axaml.cs`
- Contains: Process entry point, global styles/data templates, and DI registrations.
- Depends on: Avalonia desktop lifetime, Microsoft DI, application services,
  view-models, and views.
- Used by: The packaged Windows executable.

**Views and Rendering:**
- Purpose: Declare visual structure and translate pointer/window lifecycle events
  into view-model operations.
- Location: `SizeScanner.Avalonia/Views/`
- Contains: Compiled AXAML, minimal code-behind, and `SunburstChartControl`.
- Depends on: Avalonia, `SizeScanner.Avalonia/ViewModels/`, and
  `SizeScanner.Avalonia/Charting/`.
- Used by: `SizeScanner.Avalonia/App.axaml.cs` and `SizeScanner.Avalonia/ViewLocator.cs`.
- Rule: Keep scan, filesystem, and context-menu policy out of views. Delegate
  policy to view-models and `ChartNodeRules`.

**View-Models:**
- Purpose: Hold observable UI state, commands, cancellation ownership, and
  application workflow coordination.
- Location: `SizeScanner.Avalonia/ViewModels/`
- Contains: `MainWindowViewModel`, `ChartViewModel`, and `ViewModelBase`.
- Depends on: UI abstraction interfaces, charting types, models, and `ScannerCore`.
- Used by: AXAML compiled bindings and view code-behind.
- Rule: `MainWindowViewModel` owns root-scan/settings state;
  `ChartViewModel` owns chart scope/interaction/delete state.

**Charting Domain:**
- Purpose: Transform scan trees into a bounded display model and answer
  presentation-specific node, color, threshold, tooltip, and hit-test questions.
- Location: `SizeScanner.Avalonia/Charting/`
- Contains: Builder, segment/chart records, ring layout, hit testing, display
  metadata, node rules, threshold rules, palette, and tooltip formatting.
- Depends on: `ScannerCore/FsItem.cs` and lightweight Avalonia geometry/color types.
- Used by: `ChartViewModel`, `ChartView`, and `SunburstChartControl`.
- Rule: Use `ChartDisplayMetadata`, `ChartNodeRules`, and `FilterThreshold`; do
  not duplicate synthetic names or threshold formulas.

**Application Ports:**
- Purpose: Define mockable boundaries around operating-system and framework
  services used by view-models.
- Location: `SizeScanner.Avalonia/Abstractions/`
- Contains: `IScanService`, `ISettingsStore`, `IDriveProvider`,
  `IFileSystemActions`, `IElevationService`, `IFolderPicker`, `IDialogService`,
  and `ITopLevelProvider`.
- Depends on: Small DTO/domain types only; `IScanService` intentionally exposes
  `ScannerCore` scan types.
- Used by: View-model constructors and concrete services.

**Application Adapters:**
- Purpose: Implement the ports using Avalonia, Windows, JSON, and `ScannerCore`.
- Location: `SizeScanner.Avalonia/Services/`
- Contains: Scan scheduling, settings storage, dialog/folder picker, top-level
  registration, drive discovery, elevation, and file actions.
- Depends on: `SizeScanner.Avalonia/Abstractions/`, Avalonia APIs, Windows/.NET
  filesystem APIs, and `ScannerCore`.
- Used by: DI registrations in `SizeScanner.Avalonia/App.axaml.cs`.

**Scan Domain and Infrastructure:**
- Purpose: Enumerate the filesystem, calculate exact reachable sizes, record
  inaccessible paths, and return a bounded `FsItem` hierarchy.
- Location: `ScannerCore/`
- Contains: Public scan facade/model/contracts plus internal native cursor,
  bounded walker, retention collector, and volume policy.
- Depends on: .NET runtime and Windows native APIs only; it has no package
  dependencies.
- Used by: `SizeScanner.Avalonia/Services/ScanService.cs`,
  `ScannerConsole/Program.cs`, and both test projects.

**Executable Harness:**
- Purpose: Run a scan without Avalonia for manual correctness, progress, and
  performance checks.
- Location: `ScannerConsole/`
- Contains: A single Spectre.Console entry point.
- Depends on: `ScannerCore/` and Spectre.Console.
- Used by: Developers; it is not referenced by the production application.

**Tests:**
- Purpose: Verify native parsing boundaries, bounded/parallel scan semantics,
  chart layout and caps, view-model workflows, and service adapters.
- Location: `ScannerCore.Tests/`, `SizeScanner.Avalonia.Tests/`
- Contains: xUnit tests plus local synthetic sources, fake services, temporary
  directories, and tree factories.
- Depends on: Corresponding production projects; the UI tests also reference
  `ScannerCore/`.
- Used by: Local and GitHub CI test runs.

## Data Flow

### Primary Drive or Directory Scan

1. A toolbar command is selected through compiled AXAML bindings
   (`SizeScanner.Avalonia/Views/MainWindow.axaml:26`).
2. `MainWindowViewModel.ScanTargetAsync` creates the root cancellation source,
   switches the UI into scanning state, and calls `IScanService.RunAsync`
   (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:195`).
3. `ScanService.RunAsync` replaces the root `DriveScanner` and executes its
   synchronous scan on the thread pool (`SizeScanner.Avalonia/Services/ScanService.cs:18`).
4. `DriveScanner` distinguishes drive from directory semantics, initializes
   progress/inaccessible state, and invokes the selected engine with a
   `ScanTreeBudget` (`ScannerCore/DriveScanner.cs:58`,
   `ScannerCore/DriveScanner.cs:76`).
5. `ScanEngineSelector` chooses the first capable engine, preserves cancellation,
   and falls back after non-cancellation engine failures
   (`ScannerCore/ScanEngineSelector.cs:29`).
6. `DirectoryWalkEngine` creates a `DirectoryScanner` using allocation size for
   drive scans or logical size for directory scans, asks
   `VolumeParallelismPolicy` whether depth-limited fan-out is safe, and delegates to
   `BoundedDirectoryWalker` (`ScannerCore/DirectoryWalkEngine.cs:27`).
7. `DirectoryScanner` opens a Windows directory handle and streams
   `FILE_DIRECTORY_INFORMATION` batches from `NtQueryDirectoryFile` into an
   `IDirectoryEntrySink` (`ScannerCore/DirectoryScanner.cs:81`,
   `ScannerCore/DirectoryScanner.cs:153`).
8. `BoundedDirectoryWalker` walks post-order, calculates exact totals, records a
   capped inaccessible-path sample, and retains only budgeted children through
   `BoundedChildCollector` (`ScannerCore/BoundedDirectoryWalker.cs:28`,
   `ScannerCore/BoundedDirectoryWalker.cs:63`).
9. For a drive scan, `DriveScanner` prepends `[Free space]` and `[Inaccessible]`
   nodes using `DriveScanMetadata` (`ScannerCore/DriveScanner.cs:63`).
10. The view-model records inaccessible paths, passes the root into
    `ChartViewModel.SetScan`, and requests a refresh
    (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:229`).
11. `ChartViewModel` chooses the display root, computes the threshold, and invokes
    `SunburstChartBuilder.Build` (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:89`).
12. The builder returns a capped `SunburstChart`; `SunburstChartControl` caches
    segment geometries and renders the rings (`SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs:36`,
    `SizeScanner.Avalonia/Views/SunburstChartControl.cs:99`).

### Scoped Drill-Down

1. `ChartView` hit-tests a left click and asks `ChartViewModel.TryScopeAtAsync`
   to scope the resolved node (`SizeScanner.Avalonia/Views/ChartView.axaml.cs:105`).
2. `ChartNodeRules.IsScopable` rejects files, denied directories, and synthetic
   or fully empty nodes while allowing bounded nodes with hidden descendants
   (`SizeScanner.Avalonia/Charting/ChartNodeRules.cs:39`).
3. `ChartViewModel` constructs the absolute path and calls
   `IScanService.RunScopeAsync`, preserving allocation-size semantics when the
   root originated from a drive scan
   (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:123`).
4. `ScanService.RunScopeAsync` uses a fresh throwaway `DriveScanner`; it does not
   mutate root target, root scan kind, or root inaccessible state
   (`SizeScanner.Avalonia/Services/ScanService.cs:36`).
5. The new scope tree replaces the prior scope tree and the layout is rebuilt.
   No scope history is retained (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:137`).

### Delete and Refresh

1. A right-click target is resolved by `ChartView`, while
   `ChartNodeRules.SuppressesContextMenu` prevents operations on synthetic
   segments (`SizeScanner.Avalonia/Views/ChartView.axaml.cs:75`).
2. `ChartViewModel` confirms through `IDialogService` and delegates the operation
   to `IFileSystemActions.DeleteAsync`
   (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:275`).
3. `WindowsFileSystemActions` chooses file/directory and recycle/permanent APIs,
   returning a `DeleteResult` instead of leaking exceptions into the view-model
   (`SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs:22`).
4. A successful delete removes the node and subtracts its size from ancestors.
   A delete in an independently rescanned scope marks the cached root stale;
   returning to root triggers a root rescan
   (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:316`).

### Settings Lifecycle

1. `MainWindowViewModel` loads one mutable `UserSettings` snapshot during
   construction (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:36`).
2. Initialization applies drive choices and display/window settings; option
   changes update the snapshot and persist it
   (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:128`,
   `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:312`).
3. `MainWindow` captures final dimensions on close
   (`SizeScanner.Avalonia/Views/MainWindow.axaml.cs:30`).
4. `JsonSettingsStore` serializes through the source-generated context to
   `%AppData%\SizeScanner\settings.avalonia.json`
   (`SizeScanner.Avalonia/Services/JsonSettingsStore.cs:16`,
   `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs:9`).

**State Management:**
- DI registrations in `SizeScanner.Avalonia/App.axaml.cs` are singletons for the
  desktop lifetime.
- `MainWindowViewModel` owns root scan state, root cancellation, drive list,
  inaccessible-path presentation, and the in-memory settings snapshot.
- `ChartViewModel` owns the base/scoped tree references, root-staleness marker,
  display options, chart layout, scope cancellation, hover, and context target.
- `ScanService` owns root rescan metadata and the most recent root
  `DriveScanner`; scope scans deliberately use independent scanners.
- `FsItem.Parent` links support path reconstruction and ancestor size updates.
  `Items == null` means a directory could not be opened; an empty list means it
  was opened but retained no children. `HasUnretainedChildren` preserves
  drill-down eligibility for bounded snapshots.

## Key Abstractions

**`IScanEngine` and `ScanResult`:**
- Purpose: Define equivalent scan implementations and their complete output.
- Examples: `ScannerCore/IScanEngine.cs`,
  `ScannerCore/DirectoryWalkEngine.cs`,
  `ScannerCore/ScanEngineSelector.cs`
- Pattern: Strategy plus ordered fallback/decorator-like selector.

**Directory Cursor/Source/Sink:**
- Purpose: Separate native batched enumeration from traversal and make the
  walker testable with synthetic sources.
- Examples: `ScannerCore/DirectoryEntryCursor.cs`,
  `ScannerCore/DirectoryScanner.cs`,
  `ScannerCore.Tests/SyntheticDirectoryEntrySource.cs`
- Pattern: Pull-based cursor feeding a callback sink; interfaces are internal
  and exposed to tests with `ScannerCore/AssemblyInfo.cs`.

**`ScanTreeBudget`:**
- Purpose: Make memory, retained depth/width, inaccessible-path sampling, and
  fan-out depth and shared parallelism degree explicit inputs.
- Examples: `ScannerCore/ScanTreeBudget.cs`,
  `ScannerCore/BoundedDirectoryWalker.cs`
- Pattern: Immutable policy value with validated constructor defaults.

**`FsItem`:**
- Purpose: Carry the scan hierarchy and exact aggregate sizes across projects.
- Examples: `ScannerCore/FsItem.cs`,
  `ScannerCore/DriveScanMetadata.cs`
- Pattern: Mutable tree node during construction/deletion, with immutable name
  and kind and explicit aggregate-node identity.

**UI Service Interfaces:**
- Purpose: Keep view-model behavior independently testable and constrain
  framework/OS access to adapters.
- Examples: `SizeScanner.Avalonia/Abstractions/IScanService.cs`,
  `SizeScanner.Avalonia/Abstractions/IFileSystemActions.cs`,
  `SizeScanner.Avalonia/Abstractions/ISettingsStore.cs`
- Pattern: Ports and adapters with constructor injection.

**`SunburstChart` / `SunburstSegment`:**
- Purpose: Separate chart layout data from rendering and interaction.
- Examples: `SizeScanner.Avalonia/Charting/SunburstChart.cs`,
  `SizeScanner.Avalonia/Charting/SunburstSegment.cs`
- Pattern: Immutable records with a lazily built per-ring hit-test index.

**Central Metadata and Rules:**
- Purpose: Give synthetic nodes and display policy one source of truth.
- Examples: `ScannerCore/DriveScanMetadata.cs`,
  `SizeScanner.Avalonia/Charting/ChartDisplayMetadata.cs`,
  `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`,
  `SizeScanner.Avalonia/Charting/FilterThreshold.cs`
- Pattern: Stateless policy/metadata classes shared by builder, view-model, and
  tests.

## Entry Points

**Production Desktop:**
- Location: `SizeScanner.Avalonia/Program.cs`
- Triggers: Launching `SizeScanner.Avalonia.exe`.
- Responsibilities: Enter STA, configure Avalonia platform/font/trace logging,
  and start the classic desktop lifetime.

**Application Composition:**
- Location: `SizeScanner.Avalonia/App.axaml.cs`
- Triggers: Avalonia framework initialization.
- Responsibilities: Build the service provider and resolve the main window.

**Main Window Lifecycle:**
- Location: `SizeScanner.Avalonia/Views/MainWindow.axaml.cs`
- Triggers: Main window construction, open, and close.
- Responsibilities: Set `DataContext`, register the top-level owner, initialize
  drives/settings, and persist final dimensions.

**Manual Console Harness:**
- Location: `ScannerConsole/Program.cs`
- Triggers: `dotnet run --project ScannerConsole/ScannerConsole.csproj -- <path>`.
- Responsibilities: Run a directory scan on a worker thread and display status,
  totals, retained-node count, and inaccessible paths.

**Public Core API:**
- Location: `ScannerCore/DriveScanner.cs`
- Triggers: Calls to `ScanDrive` or `ScanDirectory` from any host.
- Responsibilities: Normalize scan mode, select the engine, throttle progress,
  expose inaccessible metadata, and decorate drive roots.

**Test Entry Points:**
- Location: `ScannerCore.Tests/ScannerCore.Tests.csproj`,
  `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`
- Triggers: `dotnet test`.
- Responsibilities: Discover and execute xUnit v3 tests for the core and UI
  architecture.

## Architectural Constraints

- **Platform:** Every project targets `net10.0-windows`; native enumeration and
  volume detection require Windows APIs. Keep the Windows boundary explicit in
  `ScannerCore/` and `SizeScanner.Avalonia/Services/`.
- **Threading:** Avalonia starts on an STA UI thread. `ScanService` moves scans
  to `Task.Run`; progress uses `Progress<ScanProgress>` to return updates to the
  captured UI context. `BoundedDirectoryWalker` fans directories out with
  `Task.Run` under a scan-wide semaphore slot budget for the first
  `ScanTreeBudget.ParallelFanOutLevels` levels (default `1`, root only; `2`/`3`
  remain explicit, tested knobs), and only when `VolumeParallelismPolicy`
  reports no seek penalty. Deeper subtrees and non-SSD volumes walk fully
  sequentially on one shared slot; there are no channels or a fixed worker
  pool. `ScanTreeBudget.MaxDegreeOfParallelism` bounds concurrent native reads
  and outstanding buffer rentals, not open-cursor count, since a parent
  cursor can stay open across an awaited child.
- **Scan serialization:** Root and scope workflows must not race a shared,
  stateful root `DriveScanner`. `MainWindowViewModel.IsBusy`,
  `ChartViewModel.IsScopeScanning`, and `IsRootScanInProgress` enforce this.
- **Memory bound:** Exact byte totals do not imply a complete tree.
  `BoundedDirectoryWalker` retains a bounded subset and aggregates hidden
  children. Consumers must preserve `HasUnretainedChildren` and aggregate
  semantics.
- **Size semantics:** Drive-rooted scans use allocation size; ordinary directory
  scans use logical end-of-file size. Scope rescans under drive trees pass
  `preferAllocatedSize: true`.
- **Reparse points:** Native parsing skips reparse points unless the entry has
  `FILE_ATTRIBUTE_OFFLINE`, preserving OneDrive online-only placeholders.
- **Denied directories:** Failure to open a directory is represented by
  `FsItem.Items == null` and a capped inaccessible-path sample, not by dropping
  the node or aborting the scan.
- **AOT/trimming:** `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj` enables
  native AOT, trimming, single-file publish, and compiled bindings. Use
  source-generated serializers such as
  `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs`; avoid reflection or
  dynamic activation in production paths.
- **Global state:** Application services and view-models are DI singletons for
  one desktop lifetime. The only mutable static state in normal flows is
  framework/property metadata and immutable singleton policies/defaults; scan
  state remains instance-owned.
- **Circular dependencies:** The project graph is acyclic. Within the UI,
  views depend on view-model/chart types, view-models depend on abstractions and
  charting, services implement abstractions, and `App` alone composes concrete
  types.
- **Synthetic identity:** Use `DriveScanMetadata` for core drive entries and
  `ChartDisplayMetadata`/`ChartNodeRules` for chart-only entries. Synthetic
  segments do not represent actionable filesystem paths.

## Anti-Patterns

### Native Filesystem Calls from UI Code

**What happens:** A view or view-model directly calls Win32 enumeration or
volume APIs.
**Why it's wrong:** It reverses the dependency boundary, makes UI behavior
harder to test, and can duplicate drive/directory size and reparse-point rules.
**Do this instead:** Put scan-native logic in `ScannerCore/DirectoryScanner.cs`
or `ScannerCore/VolumeParallelismPolicy.cs`; expose UI operations through an
interface in `SizeScanner.Avalonia/Abstractions/` and an adapter in
`SizeScanner.Avalonia/Services/`.

### Treating Aggregate/Synthetic Nodes as Paths

**What happens:** `[Other]`, `[Filtered]`, `[Free space]`, `[Inaccessible]`, or
an `FsItemKind.Aggregate` is passed to scope, Explorer, or delete behavior.
**Why it's wrong:** These nodes represent metadata or multiple hidden paths and
cannot be mapped to one safe filesystem target.
**Do this instead:** Route all policy through
`SizeScanner.Avalonia/Charting/ChartNodeRules.cs` and central names through
`ScannerCore/DriveScanMetadata.cs` and
`SizeScanner.Avalonia/Charting/ChartDisplayMetadata.cs`.

### Building an Unbounded Full Tree

**What happens:** Enumeration stores every file and directory or retains every
scope visited.
**Why it's wrong:** Wide/deep filesystems can exhaust managed memory and make
chart layout unbounded.
**Do this instead:** Stream entries through
`ScannerCore/DirectoryEntryCursor.cs`, retain with
`ScannerCore/BoundedChildCollector.cs`, enforce `ScanTreeBudget`, and replace
rather than stack scope trees in
`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.

### Concurrent Root Scans on the Singleton Service

**What happens:** More than one workflow invokes `IScanService.RunAsync` while
another root scan is active.
**Why it's wrong:** `ScanService` replaces its `Scanner`, `LastTarget`, and
`IsDriveScan`; `DriveScanner` also carries mutable progress/result state.
**Do this instead:** Preserve the busy gates in
`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` and
`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`; use
`RunScopeAsync` for independent throwaway scope work.

### Duplicating Chart Policy in Views

**What happens:** AXAML or code-behind hard-codes threshold math, synthetic
names, scoping eligibility, or context-menu rules.
**Why it's wrong:** Builder, hit testing, and actions can disagree about the
same segment.
**Do this instead:** Keep event translation in
`SizeScanner.Avalonia/Views/ChartView.axaml.cs` and policy in
`FilterThreshold`, `ChartNodeRules`, `ChartDisplayMetadata`, and
`ChartViewModel`.

## Error Handling

**Strategy:** Preserve cancellation as control flow, convert expected filesystem
limitations into explicit domain state/results, and handle user-facing failures
at the application boundary.

**Patterns:**
- `OperationCanceledException` is rethrown by engine selection and handled only
  by the workflow that owns the relevant cancellation token
  (`ScannerCore/ScanEngineSelector.cs`,
  `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`,
  `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`).
- A directory handle that cannot be opened becomes an inaccessible directory
  with `Items == null`; it is sampled in `ScanResult.Inaccessible`
  (`ScannerCore/BoundedDirectoryWalker.cs`).
- A failed native enumeration batch becomes an `IOException` so
  `ScanEngineSelector` can try a fallback engine
  (`ScannerCore/BoundedDirectoryWalker.cs`,
  `ScannerCore/ScanEngineSelector.cs`).
- Scoped scan exceptions are displayed through `IDialogService`, while the
  current chart remains unchanged
  (`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`).
- File deletion catches exceptions in the Windows adapter and returns
  `DeleteResult`; the view-model displays the error
  (`SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`).
- Settings load is fail-safe and returns defaults for missing, malformed, or
  unreadable JSON (`SizeScanner.Avalonia/Services/JsonSettingsStore.cs`).
- Elevation converts UAC cancellation and launch failures into a false result
  plus optional error text
  (`SizeScanner.Avalonia/Services/WindowsElevationService.cs`).

## Cross-Cutting Concerns

**Logging:** Avalonia logs to trace from `SizeScanner.Avalonia/Program.cs`.
Core engine fallback and native enumeration failures write diagnostic messages
with `Debug.WriteLine` in `ScannerCore/ScanEngineSelector.cs` and
`ScannerCore/DirectoryScanner.cs`. There is no application logging framework.

**Validation:** `ScanTreeBudget` validates every bound at construction;
`DirectoryScanner` checks native statuses and record boundaries; chart builders
clamp non-positive sizes and enforce both global and per-sector segment caps.
User operations are guarded by node rules, busy state, cancellation tokens, and
confirmation dialogs.

**Authentication:** Not applicable. The application has no accounts or remote
identity. Windows process identity is used only to detect administrator status
and optionally relaunch with the `runas` verb.

**Configuration:** Build/runtime platform settings are centralized in
`Directory.Build.props`, `global.json`, project files, and
`SizeScanner.Avalonia/app.manifest`. User display/window settings live in
`UserSettings` and `JsonSettingsStore`.

**Testing Boundaries:** Core native traversal is tested through internal cursor
interfaces and synthetic sources (`ScannerCore.Tests/`). UI workflows are
tested through abstraction fakes such as
`SizeScanner.Avalonia.Tests/FakeScanService.cs`; chart algorithms are tested
without rendering a window.

---

*Architecture analysis: 2026-08-06*
