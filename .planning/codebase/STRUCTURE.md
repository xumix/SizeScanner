# Codebase Structure

**Analysis Date:** 2026-08-06

## Directory Layout

```text
SizeScanner/
├── .github/
│   ├── dependabot.yml                 # Dependency update policy
│   └── workflows/                     # Windows build, release, CodeQL, auto-merge
├── .planning/
│   └── codebase/                      # GSD codebase reference documents
├── .vscode/
│   ├── launch.json                    # Avalonia debug profile
│   └── tasks.json                     # Build, publish, and watch tasks
├── docs/
│   └── superpowers/
│       ├── plans/                     # Dated implementation plans
│       └── specs/                     # Dated design specifications
├── Img/
│   └── main_window.png                # README screenshot
├── ScannerCore/                       # Filesystem scan domain and Windows engine
│   ├── AssemblyInfo.cs
│   ├── BoundedChildCollector.cs
│   ├── BoundedDirectoryWalker.cs
│   ├── DirectoryEntryCursor.cs
│   ├── DirectoryScanner.cs
│   ├── DirectoryWalkEngine.cs
│   ├── DriveScanMetadata.cs
│   ├── DriveScanner.cs
│   ├── FsItem.cs
│   ├── Humanize.cs
│   ├── IScanEngine.cs
│   ├── ScanEngineSelector.cs
│   ├── ScanProgress.cs
│   ├── ScanTreeBudget.cs
│   ├── ScannerCore.csproj
│   └── VolumeParallelismPolicy.cs
├── ScannerCore.Tests/                 # xUnit tests for ScannerCore
│   ├── *Tests.cs                      # Behavior test classes
│   ├── RecordingEntrySink.cs          # Native cursor test helper
│   ├── SyntheticDirectoryEntrySource.cs
│   ├── TemporaryDirectory.cs
│   └── ScannerCore.Tests.csproj
├── SizeScanner.Avalonia/              # Production Windows desktop application
│   ├── Abstractions/                  # Mockable application service contracts
│   ├── Assets/                        # Packaged Avalonia resources
│   ├── Charting/                      # Sunburst layout, rules, and hit testing
│   ├── Models/                        # UI/settings DTOs
│   ├── Services/                      # Avalonia/Windows/core adapters
│   ├── ViewModels/                    # MVVM state and commands
│   ├── Views/                         # AXAML, code-behind, custom chart control
│   ├── App.axaml                      # Global styles and data templates
│   ├── App.axaml.cs                   # DI composition root
│   ├── Program.cs                     # Desktop process entry point
│   ├── ViewLocator.cs                 # View-model-to-view template mapping
│   ├── app.manifest                   # Windows compatibility/UAC metadata
│   └── SizeScanner.Avalonia.csproj
├── SizeScanner.Avalonia.Tests/        # xUnit tests for UI/chart/services
│   ├── *Tests.cs                      # Behavior test classes
│   ├── FakeScanService.cs             # View-model scan fake
│   ├── TempDir.cs                     # Settings/filesystem test fixture
│   ├── TestTree.cs                    # FsItem tree factory
│   └── SizeScanner.Avalonia.Tests.csproj
├── ScannerConsole/
│   ├── Program.cs                     # Manual scan/progress/performance harness
│   └── ScannerConsole.csproj
├── .editorconfig                      # Repository formatting rules
├── .gitignore                         # Generated/local artifact exclusions
├── AGENTS.md                          # Repository guidance for coding agents
├── Directory.Build.props              # Shared compiler/project properties
├── Directory.Packages.props           # Central NuGet versions
├── global.json                        # .NET 10 SDK selection
├── README.md                          # Product overview and quick start
├── LICENSE                            # AGPL-3.0-or-later license
└── SizeScanner.slnx                   # Five-project solution
```

The canonical map is based on tracked files in the repository root. Nested
repositories under `.worktrees/` and generated `bin/` and `obj/` trees are not
part of this layout.

## Directory Purposes

**`ScannerCore/`:**
- Purpose: Own all filesystem scan semantics, native interop, retained tree
  construction, progress, and shared scan-domain types.
- Contains: Public facade/model types and internal engine/enumeration/walker
  implementations. The project intentionally keeps a flat source layout.
- Key files: `ScannerCore/DriveScanner.cs`, `ScannerCore/FsItem.cs`,
  `ScannerCore/DirectoryScanner.cs`, `ScannerCore/BoundedDirectoryWalker.cs`,
  `ScannerCore/ScanTreeBudget.cs`, `ScannerCore/ScannerCore.csproj`
- Placement rule: Put filesystem logic and Win32/native scan code here, never in
  the Avalonia project.

**`ScannerCore.Tests/`:**
- Purpose: Verify `ScannerCore/` behavior, including native record parsing,
  bounded retention, memory behavior, cancellation, error/fallback behavior, and
  SSD-gated top-level parallelism.
- Contains: Flat xUnit test classes and internal test doubles/fixtures.
- Key files: `ScannerCore.Tests/DirectoryScannerParsingTests.cs`,
  `ScannerCore.Tests/BoundedDirectoryWalkerTests.cs`,
  `ScannerCore.Tests/BoundedScanMemoryTests.cs`,
  `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`,
  `ScannerCore.Tests/SyntheticDirectoryEntrySource.cs`
- Placement rule: Name production-type tests `<ProductionType>Tests.cs`; keep
  reusable synthetic cursors and temporary filesystem helpers as standalone
  files in this project.

**`SizeScanner.Avalonia/`:**
- Purpose: Provide the production Windows desktop host, MVVM workflows,
  sunburst visualization, user settings, and filesystem actions.
- Contains: Root bootstrap/composition files plus responsibility-oriented
  subdirectories.
- Key files: `SizeScanner.Avalonia/Program.cs`,
  `SizeScanner.Avalonia/App.axaml.cs`,
  `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`,
  `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`,
  `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`
- Placement rule: Keep the root limited to host/composition files. Place feature
  code in the subdirectory matching its architectural role.

**`SizeScanner.Avalonia/Abstractions/`:**
- Purpose: Define ports used by view-models for scans, settings, drives, dialogs,
  folder selection, elevation, top-level access, and filesystem mutation.
- Contains: One public interface per file, plus tightly coupled result records
  such as `DeleteResult`.
- Key files: `SizeScanner.Avalonia/Abstractions/IScanService.cs`,
  `SizeScanner.Avalonia/Abstractions/IFileSystemActions.cs`,
  `SizeScanner.Avalonia/Abstractions/ISettingsStore.cs`
- Placement rule: Add an interface here before introducing a new OS/framework
  dependency into a view-model.

**`SizeScanner.Avalonia/Services/`:**
- Purpose: Implement `Abstractions/` using Avalonia, Windows/.NET APIs,
  `ScannerCore`, and local JSON storage.
- Contains: One concrete adapter per service plus source-generated serialization
  context.
- Key files: `SizeScanner.Avalonia/Services/ScanService.cs`,
  `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`,
  `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`,
  `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs`
- Placement rule: Pair new adapters with an interface in `Abstractions/` and
  register them in `SizeScanner.Avalonia/App.axaml.cs`.

**`SizeScanner.Avalonia/ViewModels/`:**
- Purpose: Own observable state, commands, cancellation, and application
  workflow policy.
- Contains: CommunityToolkit.Mvvm `ObservableObject`/source-generator types.
- Key files: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`,
  `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`,
  `SizeScanner.Avalonia/ViewModels/ViewModelBase.cs`
- Placement rule: Keep root-scan and global settings state in
  `MainWindowViewModel`; keep chart scope, hover, context target, and delete
  behavior in `ChartViewModel`.

**`SizeScanner.Avalonia/Views/`:**
- Purpose: Declare the visual tree, bind commands/state, translate pointer and
  window events, and render the chart.
- Contains: `*.axaml`/`*.axaml.cs` pairs, the custom `SunburstChartControl`,
  and the app-owned `BusySpinnerControl`.
- Key files: `SizeScanner.Avalonia/Views/MainWindow.axaml`,
  `SizeScanner.Avalonia/Views/ChartView.axaml`,
  `SizeScanner.Avalonia/Views/SunburstChartControl.cs`,
  `SizeScanner.Avalonia/Views/BusySpinnerControl.cs`
- Placement rule: Use code-behind only for framework lifecycle, event-to-VM
  translation, hit-test plumbing, and rendering. Put business policy in
  view-models or chart rules. `SizeScanner.Avalonia/Views/ChartView.axaml` is
  the sole owner of the scan-busy overlay (dimming border, pointer/keyboard
  blocking, and `BusySpinnerControl` composition) bound to
  `ChartViewModel.IsChartScanning`.

**`SizeScanner.Avalonia/Charting/`:**
- Purpose: Convert scan trees into a bounded sunburst display model and provide
  chart-only metadata, rules, color, threshold, tooltip, ring, and hit-test
  behavior.
- Contains: Algorithmic classes and immutable chart/segment records; no AXAML.
- Key files: `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`,
  `SizeScanner.Avalonia/Charting/SunburstChart.cs`,
  `SizeScanner.Avalonia/Charting/SunburstHitTest.cs`,
  `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`,
  `SizeScanner.Avalonia/Charting/FilterThreshold.cs`
- Placement rule: Add layout and chart policy here. Put drawing APIs in
  `Views/SunburstChartControl.cs`.

**`SizeScanner.Avalonia/Models/`:**
- Purpose: Hold simple application DTOs that are neither scan-domain nodes nor
  chart layout records.
- Contains: `DriveItem` and persisted `UserSettings`.
- Key files: `SizeScanner.Avalonia/Models/DriveItem.cs`,
  `SizeScanner.Avalonia/Models/UserSettings.cs`
- Placement rule: Keep filesystem hierarchy types in `ScannerCore/`; keep chart
  output records in `Charting/`.

**`SizeScanner.Avalonia/Assets/`:**
- Purpose: Package resources into the Avalonia application.
- Contains: `SizeScanner.Avalonia/Assets/main.ico`.
- Key files: `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`
- Placement rule: Add runtime application icons/images here and include them
  through the existing `AvaloniaResource` glob.

**`SizeScanner.Avalonia.Tests/`:**
- Purpose: Test view-model workflows, chart algorithms and caps, hit testing,
  settings, service boundaries, Windows file actions, and custom Avalonia
  control/view composition.
- Contains: Flat xUnit test classes plus local fakes, tree/temp-directory
  factories, and a dedicated Avalonia-dispatcher test thread.
- Key files: `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`,
  `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`,
  `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`,
  `SizeScanner.Avalonia.Tests/BusySpinnerControlTests.cs`,
  `SizeScanner.Avalonia.Tests/ViewLocatorTests.cs`,
  `SizeScanner.Avalonia.Tests/AvaloniaUiThread.cs`,
  `SizeScanner.Avalonia.Tests/FakeScanService.cs`,
  `SizeScanner.Avalonia.Tests/TestTree.cs`
- Placement rule: Mirror the production type name with a `Tests` suffix and
  reuse the local fake/factory files rather than adding test hooks to
  production. Route any test that constructs or reads a property on an
  `AvaloniaObject`-derived type through `AvaloniaUiThread.Invoke` to avoid
  Dispatcher thread-affinity flakiness.

**`ScannerConsole/`:**
- Purpose: Exercise `ScannerCore` without the desktop UI.
- Contains: One console entry point and project definition.
- Key files: `ScannerConsole/Program.cs`,
  `ScannerConsole/ScannerConsole.csproj`
- Placement rule: Keep manual diagnostics, timing, and progress presentation
  here; reusable scan behavior belongs in `ScannerCore/`.

**`.github/`:**
- Purpose: Configure dependency updates, Windows CI, release packaging, and
  CodeQL analysis.
- Contains: `dependabot.yml` and YAML workflows.
- Key files: `.github/workflows/dotnet-desktop.yml`,
  `.github/workflows/release.yml`, `.github/workflows/codeql.yml`,
  `.github/workflows/dependabot-auto-merge.yml`

**`.planning/codebase/`:**
- Purpose: Store the current, path-oriented architecture, stack, integration,
  convention, test, and concern maps consumed by GSD planning/execution.
- Contains: Uppercase Markdown reference documents.
- Key files: `.planning/codebase/ARCHITECTURE.md`,
  `.planning/codebase/STRUCTURE.md`

**`docs/superpowers/`:**
- Purpose: Preserve dated design specifications and implementation plans for
  substantial changes.
- Contains: `plans/` and `specs/`, with date-prefixed Markdown files.
- Key files: `docs/superpowers/plans/2026-07-16-bounded-streaming-snapshot.md`,
  `docs/superpowers/specs/2026-08-06-shallow-parallel-fanout-design.md`

**`.vscode/`:**
- Purpose: Supply repository-local launch and task definitions.
- Contains: An Avalonia debug profile and build/publish/watch tasks.
- Key files: `.vscode/launch.json`, `.vscode/tasks.json`

**`Img/`:**
- Purpose: Store documentation imagery.
- Contains: `Img/main_window.png`, referenced by `README.md`.
- Placement rule: Use `SizeScanner.Avalonia/Assets/` for packaged application
  resources and `Img/` for repository documentation only.

## Key File Locations

**Entry Points:**
- `SizeScanner.Avalonia/Program.cs`: Production desktop `Main` method.
- `SizeScanner.Avalonia/App.axaml.cs`: Application composition root.
- `SizeScanner.Avalonia/Views/MainWindow.axaml.cs`: Main window lifecycle entry.
- `ScannerConsole/Program.cs`: Manual scanner harness.
- `ScannerCore/DriveScanner.cs`: Public scan API for hosts.

**Configuration:**
- `SizeScanner.slnx`: Solution project membership and CPU platforms.
- `global.json`: Required .NET SDK baseline.
- `Directory.Build.props`: Shared nullable, analyzer, deterministic-build,
  implicit-using, and licensing settings.
- `Directory.Packages.props`: Central NuGet package versions.
- `.editorconfig`: UTF-8, whitespace, indentation, braces, and using-order rules.
- `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`: Avalonia packages,
  Windows runtime, resources, native AOT, trimming, and single-file publish.
- `SizeScanner.Avalonia/app.manifest`: UAC level and Windows compatibility.
- `.vscode/launch.json`: Debug target.
- `.vscode/tasks.json`: Developer build/publish/watch commands.

**Core Logic:**
- `ScannerCore/DriveScanner.cs`: Scan facade, progress, and drive decoration.
- `ScannerCore/IScanEngine.cs`: Engine and result contracts.
- `ScannerCore/ScanEngineSelector.cs`: Ordered engine fallback.
- `ScannerCore/DirectoryWalkEngine.cs`: Native walker configuration.
- `ScannerCore/DirectoryScanner.cs`: Windows native enumeration/parser.
- `ScannerCore/DirectoryEntryCursor.cs`: Internal cursor/source/sink contracts.
- `ScannerCore/BoundedDirectoryWalker.cs`: Post-order exact-total traversal.
- `ScannerCore/BoundedChildCollector.cs`: Largest-child retention and aggregate
  creation.
- `ScannerCore/ScanTreeBudget.cs`: Retention and worker limits.
- `ScannerCore/FsItem.cs`: Shared scan tree model.
- `ScannerCore/DriveScanMetadata.cs`: Core synthetic drive entries.
- `ScannerCore/VolumeParallelismPolicy.cs`: SSD/NVMe parallelism gate.

**Desktop Workflow:**
- `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`: Root scan/settings
  orchestration.
- `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`: Scope/chart/action
  orchestration.
- `SizeScanner.Avalonia/Abstractions/IScanService.cs`: UI-to-core scan boundary.
- `SizeScanner.Avalonia/Services/ScanService.cs`: Background scan adapter.
- `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`: User settings storage.
- `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`: Explorer/delete
  operations.

**Charting:**
- `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`: Tree-to-segment layout.
- `SizeScanner.Avalonia/Charting/SunburstChart.cs`: Chart output and ring index.
- `SizeScanner.Avalonia/Charting/SunburstHitTest.cs`: Polar hit testing.
- `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`: Synthetic/scoping/action
  policy.
- `SizeScanner.Avalonia/Charting/ChartDisplayMetadata.cs`: Chart-only synthetic
  labels.
- `SizeScanner.Avalonia/Charting/FilterThreshold.cs`: Shared filter math.
- `SizeScanner.Avalonia/Views/SunburstChartControl.cs`: Avalonia rendering and
  geometry caching.
- `SizeScanner.Avalonia/Views/ChartView.axaml`: Chart host and scan-busy overlay
  owner (dimming, pointer/keyboard blocking, spinner composition).
- `SizeScanner.Avalonia/Views/BusySpinnerControl.cs`: Dependency-free circular
  busy spinner used by the chart scan overlay.

**Testing:**
- `ScannerCore.Tests/ScannerCore.Tests.csproj`: Core xUnit project.
- `ScannerCore.Tests/SyntheticDirectoryEntrySource.cs`: Deterministic native
  source replacement.
- `ScannerCore.Tests/RecordingEntrySink.cs`: Parser sink helper.
- `ScannerCore.Tests/TemporaryDirectory.cs`: Core filesystem fixture.
- `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`: UI xUnit
  project.
- `SizeScanner.Avalonia.Tests/FakeScanService.cs`: Root/scope scan fake.
- `SizeScanner.Avalonia.Tests/TestTree.cs`: `FsItem` factory.
- `SizeScanner.Avalonia.Tests/TempDir.cs`: UI service filesystem fixture.
- `SizeScanner.Avalonia.Tests/AvaloniaUiThread.cs`: Dedicated background thread
  that Avalonia's `Dispatcher` pins to, so every test constructing or reading
  `AvaloniaObject`-derived types runs on one physical thread.
- `SizeScanner.Avalonia.Tests/AssemblyInfo.cs`: `CollectionBehavior` disabling
  test parallelization as defense in depth alongside `AvaloniaUiThread`.

**Documentation and Automation:**
- `README.md`: End-user/developer overview.
- `AGENTS.md`: Repository architecture and editing rules.
- `docs/superpowers/plans/`: Detailed dated implementation plans.
- `docs/superpowers/specs/`: Detailed dated designs.
- `.planning/codebase/`: Current machine-consumable codebase maps.
- `.github/workflows/`: Build, test, analysis, and release automation.

## Naming Conventions

**Files:**
- Use one primary PascalCase type per C# file and match the filename to the type:
  `ScannerCore/DriveScanner.cs`,
  `SizeScanner.Avalonia/Services/ScanService.cs`.
- Prefix interface filenames/types with `I`:
  `ScannerCore/IScanEngine.cs`,
  `SizeScanner.Avalonia/Abstractions/ISettingsStore.cs`.
- Suffix test classes/files with `Tests`:
  `ScannerCore.Tests/DriveScannerTests.cs`,
  `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- Pair Avalonia views as `<Name>.axaml` and `<Name>.axaml.cs`:
  `SizeScanner.Avalonia/Views/ChartView.axaml`,
  `SizeScanner.Avalonia/Views/ChartView.axaml.cs`.
- Name Windows-specific adapters with a `Windows` prefix:
  `SizeScanner.Avalonia/Services/WindowsElevationService.cs`,
  `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`.
- Name framework-specific adapters with an `Avalonia` prefix:
  `SizeScanner.Avalonia/Services/AvaloniaDialogService.cs`,
  `SizeScanner.Avalonia/Services/AvaloniaFolderPicker.cs`.
- Name dated design artifacts `YYYY-MM-DD-<kebab-case-topic>.md` under
  `docs/superpowers/plans/` or `docs/superpowers/specs/`.
- Keep repository reference documents uppercase under `.planning/codebase/`:
  `ARCHITECTURE.md`, `STRUCTURE.md`.

**Types and Members:**
- Use PascalCase for public/internal types, records, properties, methods, enum
  values, and constants: `ScanTreeBudget`, `MaxRetainedNodes`,
  `DirectoryBatchResult.Completed`.
- Use `I` prefixes for interfaces: `IDirectoryEntryCursor`, `IScanService`.
- Use camelCase for parameters and locals: `preferAllocatedSize`,
  `parallelizeTopLevel`.
- Use `_camelCase` for private fields: `_scanRoot`, `_scopeCts`.
- Use `Async` suffixes for asynchronous methods and commands:
  `RunScopeAsync`, `ScanTargetAsync`, `DeleteAsync`.
- CommunityToolkit-generated command names derive from private methods:
  `ScanDriveAsync` becomes `ScanDriveCommand` in
  `MainWindowViewModel`.

**Namespaces:**
- Match namespaces to project and directory:
  `ScannerCore`, `ScannerCore.Tests`,
  `SizeScanner.Avalonia.Charting`,
  `SizeScanner.Avalonia.Services`,
  `SizeScanner.Avalonia.Tests`.
- Use file-scoped namespaces in newer files. Existing block-scoped
  `namespace ScannerCore { ... }` files remain valid; follow the surrounding
  file when editing rather than mixing styles inside one file.

**Directories:**
- Use project names as top-level source/test directories:
  `ScannerCore/`, `ScannerCore.Tests/`, `SizeScanner.Avalonia/`,
  `SizeScanner.Avalonia.Tests/`.
- Use plural responsibility names inside the UI:
  `Abstractions/`, `Charting/`, `Models/`, `Services/`, `ViewModels/`, `Views/`.
- Keep core and test project source files flat unless a new cohesive subsystem
  has enough files to justify a directory and namespace.

## Where to Add New Code

**New Filesystem Scan Feature:**
- Primary code: `ScannerCore/`
- Tests: `ScannerCore.Tests/`
- Use when: The change affects enumeration, size accounting, reparse points,
  inaccessible paths, parallelism, scan budgets, or `FsItem`.
- Integration point: `ScannerCore/DriveScanner.cs` or an `IScanEngine`
  implementation selected through `ScannerCore/ScanEngineSelector.cs`.

**New Scan Engine:**
- Contract: `ScannerCore/IScanEngine.cs`
- Implementation: `ScannerCore/<EngineName>.cs`
- Selection: `ScannerCore/DriveScanner.cs`
- Tests: `ScannerCore.Tests/<EngineName>Tests.cs` and
  `ScannerCore.Tests/ScanEngineSelectorTests.cs`
- Requirement: Preserve drive allocation-size, directory logical-size,
  cancellation, inaccessible, and bounded-tree semantics.

**New Core Scan Policy or Limit:**
- Policy value: `ScannerCore/ScanTreeBudget.cs`
- Enforcement: `ScannerCore/BoundedDirectoryWalker.cs` or
  `ScannerCore/BoundedChildCollector.cs`
- Tests: `ScannerCore.Tests/ScanTreeBudgetTests.cs`,
  `ScannerCore.Tests/BoundedDirectoryWalkerTests.cs`

**New Application Service:**
- Contract: `SizeScanner.Avalonia/Abstractions/I<Name>Service.cs`
- Implementation: `SizeScanner.Avalonia/Services/<Name>Service.cs`,
  `Windows<Name>Service.cs`, or `Avalonia<Name>Service.cs`
- Registration: `SizeScanner.Avalonia/App.axaml.cs`
- Tests/fakes: `SizeScanner.Avalonia.Tests/<Name>ServiceTests.cs` and a local fake
  only when multiple view-model suites need it.

**New View-Model Workflow:**
- Main-window/global workflow: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`
- Chart/scope/node workflow: `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`
- New independent surface: `SizeScanner.Avalonia/ViewModels/<Name>ViewModel.cs`
- Tests: `SizeScanner.Avalonia.Tests/<Name>ViewModelTests.cs`
- View binding: `SizeScanner.Avalonia/Views/<Name>.axaml`

**New View or Dialog Surface:**
- Markup: `SizeScanner.Avalonia/Views/<Name>.axaml`
- Code-behind: `SizeScanner.Avalonia/Views/<Name>.axaml.cs`
- View-model: `SizeScanner.Avalonia/ViewModels/<Name>ViewModel.cs`
- Template mapping when dynamically resolved:
  `SizeScanner.Avalonia/ViewLocator.cs`
- Rule: Use compiled bindings and keep OS/business policy behind view-models and
  abstractions.

**New Chart Layout/Interaction Feature:**
- Layout algorithm/model: `SizeScanner.Avalonia/Charting/`
- Rendering only: `SizeScanner.Avalonia/Views/SunburstChartControl.cs`
- Pointer-event translation: `SizeScanner.Avalonia/Views/ChartView.axaml.cs`
- State/commands: `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`
- Tests: `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`,
  `SizeScanner.Avalonia.Tests/SunburstHitTestTests.cs`, or
  `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`

**New Synthetic Chart Node or Rule:**
- Core drive metadata: `ScannerCore/DriveScanMetadata.cs`
- Chart-only label: `SizeScanner.Avalonia/Charting/ChartDisplayMetadata.cs`
- Scoping/context/action policy:
  `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`
- Builder behavior: `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`
- Tests: `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`,
  `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`

**New Persisted Setting:**
- DTO property/default: `SizeScanner.Avalonia/Models/UserSettings.cs`
- Serialization metadata: `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs`
- Load/apply/save ownership:
  `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`
- Storage implementation: `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`
- Tests: `SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs`,
  `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`

**New Application Model:**
- Simple UI/settings DTO: `SizeScanner.Avalonia/Models/<Name>.cs`
- Filesystem hierarchy/domain type: `ScannerCore/<Name>.cs`
- Chart output/geometry type: `SizeScanner.Avalonia/Charting/<Name>.cs`

**New Utility:**
- Scan/domain formatting helper shared by hosts:
  `ScannerCore/<UtilityName>.cs`
- Chart-only helper: `SizeScanner.Avalonia/Charting/<UtilityName>.cs`
- UI platform adapter helper: `SizeScanner.Avalonia/Services/<UtilityName>.cs`
- Avoid a generic catch-all utilities directory; place helpers with the
  responsibility they support.

**New Test Fixture:**
- Core synthetic/native/filesystem helper: `ScannerCore.Tests/<Name>.cs`
- UI tree/fake/temp helper: `SizeScanner.Avalonia.Tests/<Name>.cs`
- Keep fixture internals in test projects; use
  `ScannerCore/AssemblyInfo.cs` only for deliberate internal core test access.

**New Packaged Asset:**
- Runtime resource: `SizeScanner.Avalonia/Assets/`
- Documentation image: `Img/`

**New Build or Dependency Configuration:**
- Shared compiler/project property: `Directory.Build.props`
- Central NuGet version: `Directory.Packages.props`
- Application-only build/publish setting:
  `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`
- CI workflow: `.github/workflows/`
- Developer task/debug profile: `.vscode/`

## Special Directories

**`.planning/codebase/`:**
- Purpose: Generated codebase maps consumed by planning and execution workflows.
- Generated: Yes, by codebase mapping workflows.
- Committed: Yes.
- Rule: Keep dates current and use repository-relative paths in backticks.

**`docs/superpowers/`:**
- Purpose: Long-lived implementation plans and design records.
- Generated: No; authored planning artifacts.
- Committed: Yes.

**`.github/`:**
- Purpose: GitHub automation and dependency policy.
- Generated: No.
- Committed: Yes.

**`.vscode/`:**
- Purpose: Shared IDE build/debug tasks.
- Generated: No.
- Committed: Yes.

**`SizeScanner.Avalonia/Assets/`:**
- Purpose: Resources embedded/packaged with the desktop executable.
- Generated: No.
- Committed: Yes.

**`Img/`:**
- Purpose: Documentation screenshots.
- Generated: No.
- Committed: Yes.

**`bin/` and `obj/` beneath projects:**
- Purpose: Compiled binaries and MSBuild/NuGet intermediates.
- Generated: Yes.
- Committed: No; ignored by `.gitignore`.
- Rule: Never inspect these as source or add them to codebase maps.

**`publish/`:**
- Purpose: Local publish output.
- Generated: Yes.
- Committed: No; ignored by `.gitignore`.

**`TestResults/`:**
- Purpose: Local test and coverage output.
- Generated: Yes.
- Committed: No; ignored by `.gitignore`.

**`.worktrees/`:**
- Purpose: Local isolated Git worktrees used by development agents/workflows.
- Generated: Yes, as local workspace infrastructure.
- Committed: No; ignored by `.gitignore`.
- Rule: Treat each nested tree as a separate repository and exclude it when
  mapping the canonical root.

---

*Structure analysis: 2026-08-06*
