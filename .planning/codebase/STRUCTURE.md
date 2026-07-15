# Codebase Structure

**Analysis Date:** 2026-07-16

## Directory Layout

```text
SizeScanner/
├── ScannerCore/                   # Windows scan engine and FsItem domain tree
├── ScannerCore.Tests/             # xUnit tests for scan-core behavior
├── SizeScanner.Avalonia/          # Production Avalonia desktop application
│   ├── Abstractions/              # UI-facing service ports
│   ├── Assets/                    # Embedded application assets
│   ├── Charting/                  # Sunburst transformation and hit-test logic
│   ├── Models/                    # UI/settings data models
│   ├── Services/                  # Avalonia and Windows service adapters
│   ├── ViewModels/                # MVVM state and commands
│   └── Views/                     # XAML views, code-behind, custom chart control
├── SizeScanner.Avalonia.Tests/    # xUnit tests for UI logic and services
├── ScannerConsole/                # Manual scan/performance harness
├── docs/superpowers/              # Design specifications and implementation plans
├── .planning/codebase/            # Generated GSD codebase reference documents
├── .github/                       # GitHub dependency and workflow configuration
├── .vscode/                       # Editor launch and task configuration
├── Img/                           # Repository documentation images
├── SizeScanner.slnx               # Five-project solution definition
├── Directory.Build.props          # Shared MSBuild compilation properties
├── Directory.Packages.props       # Central NuGet package versions
├── global.json                    # .NET SDK selection
├── .editorconfig                  # Repository formatting rules
├── .gitlab-ci.yml                 # GitLab Windows CI/release pipeline
├── AGENTS.md                      # Repository-specific engineering guidance
└── README.md                      # Product overview and build/run instructions
```

## Project Dependency Layout

```text
`SizeScanner.Avalonia.Tests/`
        ├──→ `SizeScanner.Avalonia/`
        └──→ `ScannerCore/`

`ScannerCore.Tests/` ─────────────→ `ScannerCore/`
`ScannerConsole/` ────────────────→ `ScannerCore/`
`SizeScanner.Avalonia/` ──────────→ `ScannerCore/`
`ScannerCore/` ───────────────────→ .NET + Windows APIs only
```

- The project graph is declared in `SizeScanner.slnx` and individual `*.csproj` files.
- Keep dependencies pointing inward toward `ScannerCore/`; do not add an Avalonia reference to `ScannerCore/ScannerCore.csproj`.
- Test projects reference their systems under test directly through `ScannerCore.Tests/ScannerCore.Tests.csproj` and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.

## Directory Purposes

**`ScannerCore/`:**
- Purpose: Own all filesystem scan behavior and the data model consumed by every front end.
- Contains: Scan facade, engine strategy, native directory enumeration, volume policy, domain tree, progress record, synthetic drive metadata, and formatting helper.
- Key files: `ScannerCore/DriveScanner.cs`, `ScannerCore/IScanEngine.cs`, `ScannerCore/DirectoryWalkEngine.cs`, `ScannerCore/DirectoryScanner.cs`, `ScannerCore/FsItem.cs`
- Placement rule: Put Win32 P/Invoke and scan-semantic changes here, not under `SizeScanner.Avalonia/`.

**`ScannerCore.Tests/`:**
- Purpose: Verify tree construction, native record parsing, engine fallback, drive metadata, progress, and parallelism policy.
- Contains: xUnit test classes and temporary-directory support.
- Key files: `ScannerCore.Tests/DriveScannerTests.cs`, `ScannerCore.Tests/DirectoryWalkEngineTests.cs`, `ScannerCore.Tests/DirectoryScannerParsingTests.cs`, `ScannerCore.Tests/ScanEngineSelectorTests.cs`
- Placement rule: Add tests here for any behavior implemented in `ScannerCore/`.

**`SizeScanner.Avalonia/`:**
- Purpose: Deliver the production Windows desktop UI and adapt the scan core to Avalonia.
- Contains: Application startup, dependency injection, MVVM classes, chart logic, services, views, resources, and manifest.
- Key files: `SizeScanner.Avalonia/Program.cs`, `SizeScanner.Avalonia/App.axaml.cs`, `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`
- Placement rule: Keep UI state and rendering here; access filesystem scanning through `ScannerCore/` and UI platform operations through `SizeScanner.Avalonia/Abstractions/`.

**`SizeScanner.Avalonia/Abstractions/`:**
- Purpose: Define constructor-injected ports for external or framework-coupled operations.
- Contains: One interface per service concern plus `DeleteResult` in `SizeScanner.Avalonia/Abstractions/IFileSystemActions.cs`.
- Key files: `SizeScanner.Avalonia/Abstractions/IScanService.cs`, `SizeScanner.Avalonia/Abstractions/ISettingsStore.cs`, `SizeScanner.Avalonia/Abstractions/IDialogService.cs`
- Placement rule: Add an interface here when a view-model needs a mockable platform/application dependency.

**`SizeScanner.Avalonia/Services/`:**
- Purpose: Implement abstraction ports using Avalonia, Windows, JSON, and the scan core.
- Contains: Scan task adapter, settings persistence, drive discovery, folder picker, dialog service, elevation service, filesystem actions, top-level registry, and JSON serializer context.
- Key files: `SizeScanner.Avalonia/Services/ScanService.cs`, `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`, `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`, `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs`
- Placement rule: Pair new implementations with an interface in `SizeScanner.Avalonia/Abstractions/` and register them in `SizeScanner.Avalonia/App.axaml.cs`.

**`SizeScanner.Avalonia/ViewModels/`:**
- Purpose: Own bindable UI state, commands, and application interaction policy.
- Contains: Shared observable base, main-window orchestration, and chart interaction state.
- Key files: `SizeScanner.Avalonia/ViewModels/ViewModelBase.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`
- Placement rule: Put user-action policy here; keep raw pointer/lifecycle plumbing in views and pure chart calculations in `SizeScanner.Avalonia/Charting/`.

**`SizeScanner.Avalonia/Views/`:**
- Purpose: Define visual composition, lifecycle hooks, pointer-event wiring, and custom drawing.
- Contains: `*.axaml` views paired with `*.axaml.cs` code-behind plus `SunburstChartControl`.
- Key files: `SizeScanner.Avalonia/Views/MainWindow.axaml`, `SizeScanner.Avalonia/Views/MainWindow.axaml.cs`, `SizeScanner.Avalonia/Views/ChartView.axaml`, `SizeScanner.Avalonia/Views/ChartView.axaml.cs`, `SizeScanner.Avalonia/Views/SunburstChartControl.cs`
- Placement rule: Add XAML UI under this directory; delegate business and interaction decisions to view-models.

**`SizeScanner.Avalonia/Charting/`:**
- Purpose: Transform scan trees into a bounded sunburst presentation model and support chart interaction.
- Contains: Segment/chart records, builder, ring geometry policy, hit testing, node rules, threshold rules, colors, display metadata, and hover text.
- Key files: `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`, `SizeScanner.Avalonia/Charting/SunburstChart.cs`, `SizeScanner.Avalonia/Charting/SunburstHitTest.cs`, `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`
- Placement rule: Add pure chart layout/display policy here; do not put filesystem traversal or window lifecycle code here.

**`SizeScanner.Avalonia/Models/`:**
- Purpose: Hold small UI and persistence DTOs that are not scan-core domain objects.
- Contains: `DriveItem` toolbar model and mutable `UserSettings`.
- Key files: `SizeScanner.Avalonia/Models/DriveItem.cs`, `SizeScanner.Avalonia/Models/UserSettings.cs`
- Placement rule: Keep filesystem hierarchy types in `ScannerCore/FsItem.cs`; reserve this directory for Avalonia application data.

**`SizeScanner.Avalonia/Assets/`:**
- Purpose: Supply embedded application resources.
- Contains: Application icon `SizeScanner.Avalonia/Assets/main.ico`.
- Key files: `SizeScanner.Avalonia/Assets/main.ico`
- Placement rule: Add UI resources under this directory so `AvaloniaResource Include="Assets\**"` in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj` embeds them.

**`SizeScanner.Avalonia.Tests/`:**
- Purpose: Verify chart transformation, hit testing, view-model behavior, services, settings, view location, and UI startup.
- Contains: xUnit test classes plus `TestTree` and temporary-directory helpers.
- Key files: `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`, `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`, `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`, `SizeScanner.Avalonia.Tests/SmokeTests.cs`
- Placement rule: Add tests here for code under `SizeScanner.Avalonia/`; prefer fakes at the test-class boundary or shared helpers such as `SizeScanner.Avalonia.Tests/TestTree.cs`.

**`ScannerConsole/`:**
- Purpose: Provide a manual performance/progress harness without loading Avalonia.
- Contains: A single executable entry point and project definition.
- Key files: `ScannerConsole/Program.cs`, `ScannerConsole/ScannerConsole.csproj`
- Placement rule: Keep this as a harness; reusable scan behavior belongs in `ScannerCore/`.

**`docs/superpowers/`:**
- Purpose: Store engineering design records and execution plans.
- Contains: Design specs under `docs/superpowers/specs/` and implementation plans under `docs/superpowers/plans/`.
- Key files: `docs/superpowers/specs/2026-06-17-scanner-optimization-design.md`, `docs/superpowers/plans/2026-06-17-scanner-optimization-phases-1-4.md`
- Placement rule: Documentation here describes planned or designed work and is not compiled.

**`.github/`:**
- Purpose: Configure GitHub dependency automation, build/test, release, and CodeQL workflows.
- Contains: `.github/dependabot.yml` and workflow YAML under `.github/workflows/`.
- Key files: `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, `.github/workflows/codeql.yml`
- Placement rule: Keep GitHub-specific automation here; GitLab automation remains in `.gitlab-ci.yml`.

## Key File Locations

**Entry Points:**
- `SizeScanner.Avalonia/Program.cs`: Production desktop process entry.
- `SizeScanner.Avalonia/App.axaml.cs`: Dependency-injection composition root and main-window construction.
- `ScannerConsole/Program.cs`: Manual scanner harness entry.
- `ScannerCore/DriveScanner.cs`: Public scan API for drives and directories.

**Configuration:**
- `SizeScanner.slnx`: Solution projects and build platforms.
- `global.json`: Pins the .NET 10 SDK baseline.
- `Directory.Build.props`: Enables nullable analysis, latest analyzers, deterministic builds, and shared license metadata.
- `Directory.Packages.props`: Centralizes package versions.
- `.editorconfig`: Defines UTF-8, final newlines, four-space C# indentation, brace placement, and `System` import ordering.
- `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`: Configures WinExe output, native AOT, trimming, single-file publish, compiled bindings, manifest, icon, and packages.
- `SizeScanner.Avalonia/app.manifest`: Windows application manifest.

**Core Logic:**
- `ScannerCore/DirectoryScanner.cs`: Native one-directory enumeration and NT record parsing.
- `ScannerCore/DirectoryWalkEngine.cs`: Recursive tree construction and SSD-gated parallelism.
- `ScannerCore/VolumeParallelismPolicy.cs`: Windows storage seek-penalty detection.
- `ScannerCore/FsItem.cs`: Shared scan tree model.
- `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`: End-to-end scan UI orchestration.
- `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`: Scope, hover, context, and delete orchestration.
- `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`: Tree-to-segment transformation.
- `SizeScanner.Avalonia/Views/SunburstChartControl.cs`: Segment-to-Avalonia geometry rendering.

**Testing:**
- `ScannerCore.Tests/`: Scanner unit and Windows integration tests.
- `SizeScanner.Avalonia.Tests/`: Chart, service, view-model, and smoke tests.
- `ScannerCore.Tests/TemporaryDirectory.cs`: Scan-core temporary filesystem helper.
- `SizeScanner.Avalonia.Tests/TestTree.cs`: Reusable chart/view-model tree factory.
- `SizeScanner.Avalonia.Tests/TempDir.cs`: Avalonia-service temporary filesystem helper.

**Continuous Integration and Release:**
- `.gitlab-ci.yml`: GitLab Windows restore, build, test, and tagged publish pipeline.
- `.github/workflows/dotnet-desktop.yml`: GitHub Windows build/test workflow.
- `.github/workflows/release.yml`: Tagged GitHub release packaging.
- `.github/workflows/codeql.yml`: GitHub CodeQL analysis.

**Repository Guidance:**
- `AGENTS.md`: Mandatory architecture, scanning, chart, settings, AOT, and placement constraints.
- `README.md`: User-facing product requirements and local commands.

## Naming Conventions

**Files:**
- Use PascalCase matching the primary C# type: `DriveScanner.cs`, `MainWindowViewModel.cs`, `SunburstChartBuilder.cs`.
- Name interfaces with an `I` prefix and pair them with concrete service names: `IScanService.cs` / `ScanService.cs`, `ISettingsStore.cs` / `JsonSettingsStore.cs`.
- Pair Avalonia views as `Name.axaml` and `Name.axaml.cs`: `SizeScanner.Avalonia/Views/ChartView.axaml` and `SizeScanner.Avalonia/Views/ChartView.axaml.cs`.
- Name xUnit classes with the production subject plus `Tests`: `ScannerCore.Tests/DriveScannerTests.cs`, `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- Name dated design documents `YYYY-MM-DD-description.md` under `docs/superpowers/specs/` or `docs/superpowers/plans/`.

**Directories:**
- Use PascalCase for .NET project and application-layer directories: `ScannerCore/`, `SizeScanner.Avalonia/`, `ViewModels/`, `Charting/`.
- Use the `.Tests` project suffix for test assemblies: `ScannerCore.Tests/`, `SizeScanner.Avalonia.Tests/`.
- Use tool-defined lowercase hidden directories for automation/configuration: `.github/`, `.vscode/`, `.planning/`.

**Namespaces:**
- Match project and directory structure: `ScannerCore`, `SizeScanner.Avalonia.ViewModels`, `SizeScanner.Avalonia.Charting`, `SizeScanner.Avalonia.Services`.
- Test namespaces follow their project roots as configured in each test `*.csproj`.

**Types and Members:**
- Use PascalCase for public/internal types, methods, properties, records, and constants exposed as domain metadata.
- Prefix private fields with `_`, as in `_scanRoot` in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`.
- Use the `Async` suffix for task-returning methods and generated commands: `ScanTargetAsync`, `DeleteAsync`, `PickFolderAsync`.
- Use `Windows` or `Avalonia` prefixes when an implementation is platform/framework-specific: `WindowsElevationService`, `AvaloniaFolderPicker`.

## Where to Add New Code

**New Filesystem Scan Feature:**
- Primary code: `ScannerCore/`
- Public contract or strategy seam: `ScannerCore/IScanEngine.cs`, `ScannerCore/DriveScanner.cs`
- Native interop: `ScannerCore/DirectoryScanner.cs` or a focused new type under `ScannerCore/`
- Tests: `ScannerCore.Tests/`

**New Scan Engine:**
- Implementation: Add a focused `*Engine.cs` under `ScannerCore/` implementing `IScanEngine`.
- Selection: Register it in the ordered engine list constructed by `ScannerCore/DriveScanner.cs`; retain `DirectoryWalkEngine` as the capable fallback.
- Tests: Add engine behavior tests under `ScannerCore.Tests/` and selection/fallback cases in `ScannerCore.Tests/ScanEngineSelectorTests.cs`.

**New Main-Window Feature:**
- Bindable state and commands: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` or a focused new view-model under `SizeScanner.Avalonia/ViewModels/`.
- Visual layout: `SizeScanner.Avalonia/Views/MainWindow.axaml` or a new paired view under `SizeScanner.Avalonia/Views/`.
- Tests: `SizeScanner.Avalonia.Tests/`.

**New Chart Behavior:**
- Tree/layout transformation: `SizeScanner.Avalonia/Charting/`.
- Interaction policy: `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` and `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`.
- Pointer/lifecycle mechanics: `SizeScanner.Avalonia/Views/ChartView.axaml.cs`.
- Drawing only: `SizeScanner.Avalonia/Views/SunburstChartControl.cs`.
- Tests: `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`, `SizeScanner.Avalonia.Tests/SunburstHitTestTests.cs`, or `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.

**New Platform or Avalonia Integration:**
- Interface: `SizeScanner.Avalonia/Abstractions/I{Name}.cs`.
- Implementation: `SizeScanner.Avalonia/Services/{PlatformOrFramework}{Name}.cs`.
- Registration: `SizeScanner.Avalonia/App.axaml.cs`.
- Tests: `SizeScanner.Avalonia.Tests/{Implementation}Tests.cs`.

**New Persisted Setting:**
- DTO property and default: `SizeScanner.Avalonia/Models/UserSettings.cs`.
- Source-generated serialization: The existing `[JsonSerializable(typeof(UserSettings))]` in `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs` covers added properties.
- Load/apply/save orchestration: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`.
- Storage tests: `SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs`.

**New UI Model:**
- Implementation: `SizeScanner.Avalonia/Models/`.
- Keep scan-tree/domain additions in `ScannerCore/` rather than `SizeScanner.Avalonia/Models/`.

**Utilities:**
- Scan/domain helper: `ScannerCore/`, near the behavior it supports.
- Chart helper: `SizeScanner.Avalonia/Charting/`.
- UI/platform helper: `SizeScanner.Avalonia/Services/` behind an abstraction when consumed by a view-model.
- Avoid a generic root-level utility directory; use the owning layer.

## Special Directories

**`.planning/codebase/`:**
- Purpose: Holds architecture and structure maps consumed by GSD planning/execution.
- Generated: Yes, by codebase mapping workflows.
- Committed: Intended as repository planning documentation.

**`docs/superpowers/`:**
- Purpose: Holds dated design specifications and implementation plans.
- Generated: No; authored planning artifacts.
- Committed: Yes.

**`bin/` and `obj/` under projects:**
- Purpose: Hold compiled binaries, generated source, restore metadata, and intermediate build output.
- Generated: Yes.
- Committed: No; excluded by `.gitignore`.

**`TestResults/`:**
- Purpose: Holds local test and coverage output.
- Generated: Yes.
- Committed: No; test-result patterns are excluded by `.gitignore`.

**`publish/`:**
- Purpose: Holds local release-like publish artifacts.
- Generated: Yes.
- Committed: No; excluded by `.gitignore`.

**`SizeScanner.Avalonia/Assets/`:**
- Purpose: Holds resources embedded into the Avalonia application.
- Generated: No.
- Committed: Yes.

**`Img/`:**
- Purpose: Holds images referenced by repository documentation such as `README.md`.
- Generated: No.
- Committed: Yes.

**`.github/workflows/`:**
- Purpose: Holds GitHub Actions pipeline definitions.
- Generated: No.
- Committed: Yes.

**`.vscode/`:**
- Purpose: Holds shared editor launch and task definitions.
- Generated: No.
- Committed: Yes.

---

*Structure analysis: 2026-07-16*
