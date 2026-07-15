# Coding Conventions

**Analysis Date:** 2026-07-16

## Naming Patterns

**Files:**
- Use PascalCase and match the principal type name: `ScannerCore/DriveScanner.cs`, `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`, and `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- Name test classes and files after the subject with a `Tests` suffix, as in `ScannerCore.Tests/DriveScannerTests.cs` and `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`.
- Keep Avalonia markup and code-behind paired by basename, for example `SizeScanner.Avalonia/Views/MainWindow.axaml` and `SizeScanner.Avalonia/Views/MainWindow.axaml.cs`.
- Put interfaces in individual `I{Name}.cs` files under `SizeScanner.Avalonia/Abstractions/`, such as `SizeScanner.Avalonia/Abstractions/IScanService.cs`.

**Functions:**
- Use PascalCase for methods and properties; append `Async` to task-returning operations such as `ScanTargetAsync` in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` and `DeleteAsync` in `SizeScanner.Avalonia/Abstractions/IFileSystemActions.cs`.
- Prefix non-throwing conditional operations with `Try`, returning `bool` and using an `out` value where needed; examples are `TryGetPathFrom` in `ScannerCore/FsItem.cs` and `TryRelaunchAsAdministrator` in `SizeScanner.Avalonia/Abstractions/IElevationService.cs`.
- Name xUnit methods as behavior statements with underscores, such as `Scan_builds_tree_with_sizes_parents_and_total` in `ScannerCore.Tests/DirectoryWalkEngineTests.cs`.
- Keep CommunityToolkit command handlers private and verb-oriented; `[RelayCommand]` generates the public command surface from methods such as `BrowseAsync`, `RescanAsync`, and `CancelScan` in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`.

**Variables:**
- Use camelCase for parameters and locals, as demonstrated throughout `ScannerCore/DirectoryWalkEngine.cs`.
- Prefix private fields with `_camelCase`, as in `_scanRoot`, `_scanCts`, and `_settingsStore` in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`.
- Use PascalCase for constants and static readonly values, including `DefaultInaccessiblePaneWidth` in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` and `FreeSpaceName` in `ScannerCore/DriveScanMetadata.cs`.
- Use named arguments when booleans would otherwise be ambiguous, for example `isDriveScan: false` and `preferAllocatedSize: false` in `ScannerCore.Tests/DirectoryWalkEngineTests.cs` and `ScannerCore.Tests/DirectoryScannerParsingTests.cs`.

**Types:**
- Use PascalCase for classes, records, structs, enums, and interfaces; interfaces carry the `I` prefix in `ScannerCore/IScanEngine.cs` and `SizeScanner.Avalonia/Abstractions/IDialogService.cs`.
- Seal concrete types unless inheritance is required; examples include `ScannerCore/FsItem.cs`, `ScannerCore/DirectoryWalkEngine.cs`, and `SizeScanner.Avalonia/Services/ScanService.cs`.
- Use records for immutable value-like data such as `ScannerCore/ScanProgress.cs`, `SizeScanner.Avalonia/Charting/SunburstSegment.cs`, and `SizeScanner.Avalonia/Models/DriveItem.cs`.
- Use `readonly record struct` for small result/value carriers such as `DeleteResult` in `SizeScanner.Avalonia/Abstractions/IFileSystemActions.cs`.

## Code Style

**Formatting:**
- Follow `/.editorconfig`: UTF-8, final newline, trimmed trailing whitespace, four-space indentation, spaces rather than tabs, and a newline before every opening brace.
- Keep `System` namespaces first, followed by framework/package imports and then project namespaces; sorting is configured by `dotnet_sort_system_directives_first` in `/.editorconfig`.
- Write explicit `using` directives because implicit usings are disabled in `/Directory.Build.props`.
- Prefer file-scoped namespaces for new files, matching `ScannerCore/DirectoryWalkEngine.cs` and most files under `SizeScanner.Avalonia/`; preserve block-scoped namespaces when modifying legacy-style files such as `ScannerCore/DirectoryScanner.cs`.
- Use modern C# collection expressions where they improve clarity, as in `Drives = []` in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` and `children = []` in `ScannerCore/DirectoryWalkEngine.cs`.
- Keep the copyright and SPDX header used by source and test files, for example `ScannerCore/FsItem.cs` and `ScannerCore.Tests/FsItemTests.cs`.

**Linting:**
- The repository relies on .NET analyzers at `AnalysisLevel=latest`, nullable reference analysis, and deterministic builds configured in `/Directory.Build.props`.
- No separate StyleCop, Roslynator, or formatter package is configured in `/Directory.Packages.props`; use `dotnet build SizeScanner.slnx` to surface compiler and analyzer diagnostics.
- Preserve native AoT and trimming compatibility configured in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj` and `ScannerCore/ScannerCore.csproj`; avoid reflection or dynamic activation in production code unless explicitly annotated and publish-tested.

## Import Organization

**Order:**
1. Import `System` namespaces first, as in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
2. Import framework and NuGet namespaces next, including Avalonia and CommunityToolkit namespaces in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`.
3. Import project namespaces last, including `ScannerCore` and `SizeScanner.Avalonia.*` in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.

**Path Aliases:**
- No C# path aliases or global-using files are configured; project references are declared directly in `ScannerCore.Tests/ScannerCore.Tests.csproj`, `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`, and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.

## Error Handling

**Patterns:**
- Thread `CancellationToken` through scanning APIs from `SizeScanner.Avalonia/Services/ScanService.cs` into `ScannerCore/DriveScanner.cs` and `ScannerCore/DirectoryWalkEngine.cs`.
- Treat cancellation separately from failure: `ScannerCore/ScanEngineSelector.cs` rethrows `OperationCanceledException`, while `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` catches it only when its active token requested cancellation and restores UI state.
- The parallel walker may catch `OperationCanceledException` and return a partial tree; callers check the token after the scan, as documented in `ScannerCore/DirectoryWalkEngine.cs` and tested in `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`.
- Model expected boundary failures as data instead of exceptions: native directory access returns `null` in `ScannerCore/DirectoryScanner.cs`, deletion returns `DeleteResult` in `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`, and elevation returns `false` plus an error string in `SizeScanner.Avalonia/Services/WindowsElevationService.cs`.
- Use conservative defaults for optional platform/configuration probes: settings load returns a default `UserSettings` in `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`, elevation detection returns `false` in `ScannerCore/DriveScanner.cs`, and seek-penalty detection returns non-parallel behavior in `ScannerCore/VolumeParallelismPolicy.cs`.
- Allow an engine selector to fall back after an unexpected engine exception, but rethrow the last failure when no engine succeeds; this contract lives in `ScannerCore/ScanEngineSelector.cs` and is exercised by `ScannerCore.Tests/ScanEngineSelectorTests.cs`.
- Release pooled/native resources in `finally` and use disposable wrappers: `ScannerCore/DirectoryScanner.cs` returns its `ArrayPool<byte>` buffer and closes its `SafeFileHandle`, while temporary test directories use `using var` in `ScannerCore.Tests/DriveScannerTests.cs`.
- Keep UI busy-state cleanup in `finally`; `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` clears deletion state whether the filesystem task succeeds or throws.

## Logging

**Framework:** Debug/trace output; no structured logging dependency is configured in `/Directory.Packages.props`.

**Patterns:**
- Write low-level diagnostic failures to `Debug.WriteLine`, as in `ScannerCore/DirectoryScanner.cs` and `ScannerCore/ScanEngineSelector.cs`.
- Route Avalonia framework diagnostics to trace with `LogToTrace()` in `SizeScanner.Avalonia/Program.cs`.
- Present actionable operation failures through result values and dialogs in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, rather than treating debug output as user feedback.

## Comments

**When to Comment:**
- Explain non-obvious platform, concurrency, chart geometry, and synthetic-node invariants; examples appear in `ScannerCore/DirectoryWalkEngine.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, and `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`.
- Avoid comments that merely restate a method name; short methods in `SizeScanner.Avalonia/Charting/FilterThreshold.cs` are self-documenting.
- Document best-effort exception suppression explicitly, as the cleanup helpers do in `ScannerCore.Tests/TemporaryDirectory.cs` and `SizeScanner.Avalonia.Tests/TempDir.cs`.

**JSDoc/TSDoc:**
- Not applicable to this C# repository. Use XML documentation for public contracts or behavior that is not obvious, as in `ScannerCore/IScanEngine.cs`, `ScannerCore/DirectoryWalkEngine.cs`, and `SizeScanner.Avalonia/Abstractions/IElevationService.cs`.

## Function Design

**Size:** Keep orchestration methods readable by extracting focused helpers, following `ScanUnitInternal`, `OnEngineProgress`, and `ReportProgress` in `ScannerCore/DriveScanner.cs`; chart construction uses dedicated helpers in `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`.

**Parameters:** Pass collaborators through constructors and behavior through small delegates, as shown by interface injection in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` and the internal `Func<string, bool>` seam in `ScannerCore/DirectoryWalkEngine.cs`.

**Return Values:** Prefer domain values for successful operations (`FsItem`, `ScanResult`, chart records), nullable values for expected absence (`DirectoryScanner.Scan` in `ScannerCore/DirectoryScanner.cs`), and explicit result records for recoverable failures (`DeleteResult` in `SizeScanner.Avalonia/Abstractions/IFileSystemActions.cs`).

## Module Design

**Exports:** Keep filesystem and Win32 logic in `ScannerCore/`; expose UI-side platform behavior through interfaces in `SizeScanner.Avalonia/Abstractions/` with implementations in `SizeScanner.Avalonia/Services/`, as required by `/AGENTS.md`.

**Barrel Files:** Not used. Reference concrete namespaces and individual types directly; dependency registration is centralized in `SizeScanner.Avalonia/App.axaml.cs`.

**Design Patterns:**
- Use MVVM with CommunityToolkit source generation for observable properties and commands in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` and `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- Keep views thin and delegate chart policy to the view model and rule helpers in `SizeScanner.Avalonia/Views/ChartView.axaml.cs` and `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`.
- Put shared synthetic-drive metadata in `ScannerCore/DriveScanMetadata.cs` and chart-only synthetic metadata in `SizeScanner.Avalonia/Charting/ChartDisplayMetadata.cs`; do not duplicate names, indices, or filter calculations, per `/AGENTS.md`.
- Use constructor injection and small interfaces to make view models testable; service registration remains centralized in `SizeScanner.Avalonia/App.axaml.cs`.
- Expose selected core internals only to the core test assembly through `InternalsVisibleTo` in `ScannerCore/AssemblyInfo.cs`.

---

*Convention analysis: 2026-07-16*
