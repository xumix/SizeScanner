# Coding Conventions

**Analysis Date:** 2026-08-06

## Naming Patterns

**Files:**
- Use PascalCase and normally match the primary type: `ScannerCore/DriveScanner.cs`, `SizeScanner.Avalonia/Services/ScanService.cs`, and `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- Name interfaces with an `I` prefix and keep one interface per file under `SizeScanner.Avalonia/Abstractions/`, for example `SizeScanner.Avalonia/Abstractions/IScanService.cs`.
- Pair Avalonia markup and code-behind as `<View>.axaml` and `<View>.axaml.cs`, as in `SizeScanner.Avalonia/Views/MainWindow.axaml` and `SizeScanner.Avalonia/Views/MainWindow.axaml.cs`.
- Name test classes and files `<Subject>Tests`; place reusable test support in purpose-named files such as `ScannerCore.Tests/TemporaryDirectory.cs`, `ScannerCore.Tests/RecordingEntrySink.cs`, and `SizeScanner.Avalonia.Tests/TestTree.cs`.

**Functions:**
- Use PascalCase for methods and properties: `ScanDrive`, `RunScopeAsync`, `CountRetainedNodes`, and `InaccessiblePathsTruncated`.
- Suffix methods that return or await `Task` with `Async`, including command handlers such as `GoToRootAsync` in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- Name command predicates `Can...` or `CanExecute...`, as in `CanStartScan` and `CanExecuteRescan` in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`.
- Name tests `Member_or_scenario_expected_behavior` with underscores, for example `ScanDirectory_honors_a_custom_budget_to_bound_retained_nodes` in `ScannerCore.Tests/DriveScannerTests.cs`.

**Variables:**
- Use camelCase for parameters and locals; use descriptive role names such as `cancellationToken`, `filterPercent`, `displayRoot`, and `scopeScanner`.
- Use `_camelCase` for private instance state in current code, as in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` and `ScannerCore/DirectoryWalkEngine.cs`.
- Use PascalCase for constants and public static values: `DefaultInaccessiblePaneWidth`, `BufferSize`, `MaxSegments`, and `ScanTreeBudget.Default`.
- Preserve Win32 names and native field casing only at the interop boundary in `ScannerCore/DirectoryScanner.cs` and `ScannerCore/VolumeParallelismPolicy.cs`; do not copy native naming into ordinary managed code.
- Prefer `var` when the initializer makes the type clear, as throughout `ScannerCore/BoundedDirectoryWalker.cs` and `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`.

**Types:**
- Use PascalCase for classes, records, structs, enums, and enum members; prefix interfaces with `I`.
- Seal concrete classes unless inheritance is required. Examples include `FsItem` in `ScannerCore/FsItem.cs`, services under `SizeScanner.Avalonia/Services/`, and test classes.
- Use records or readonly record structs for value-like data: `ScannerCore/ScanProgress.cs`, `ScannerCore/ScanTreeBudget.cs`, `SizeScanner.Avalonia/Models/DriveItem.cs`, and `DeleteResult` in `SizeScanner.Avalonia/Abstractions/IFileSystemActions.cs`.
- Name manual collaborators by behavior in tests: `Fake...` for configurable recorders, `Noop...` for benign implementations, `Failing...` for failure paths, and `Pending...` for controlled asynchronous work, as in `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.

## Code Style

**Formatting:**
- Follow `.editorconfig`: UTF-8, final newline, trimmed trailing whitespace, four spaces for C#, and spaces rather than tabs.
- Put opening braces on a new line for types, methods, and multi-line control blocks. The repository commonly omits braces for a single simple statement; match the surrounding file and add braces when a branch needs explanation or multiple operations.
- Put the two-line copyright/SPDX header at the top of every C# source and test file, as demonstrated by `ScannerCore/DriveScanner.cs` and `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`.
- Prefer file-scoped namespaces for new code, matching `ScannerCore/ScanTreeBudget.cs` and all current Avalonia files. Older core files such as `ScannerCore/DriveScanner.cs` and `ScannerCore/DirectoryScanner.cs` retain block namespaces; do not mechanically mix the two styles within a file.
- Use modern C# where it simplifies intent: collection expressions in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, primary constructors in `ScannerCore/BoundedDirectoryWalker.cs`, records in `ScannerCore/ScanProgress.cs`, property patterns in `ScannerCore.Tests/BoundedDirectoryWalkerTests.cs`, and target-typed `new`.
- Use expression-bodied members for short projections or delegates, but use block bodies for state transitions, resource management, and error handling.
- In Avalonia markup, indent nested elements by four spaces, split long attribute sets over aligned lines, use `x:DataType` for compiled bindings, and keep view policy in view-models. See `SizeScanner.Avalonia/Views/MainWindow.axaml` and `SizeScanner.Avalonia/Views/ChartView.axaml`.

**Linting:**
- The .NET SDK analyzers run at `AnalysisLevel=latest` through `Directory.Build.props`; nullable reference analysis is enabled and implicit global usings are disabled.
- No StyleCop configuration, custom ruleset, `.runsettings`, or separate formatter configuration is present. `.editorconfig` is the repository's explicit style source.
- `ScannerCore/ScannerCore.csproj` and `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj` declare AOT compatibility; the application also enables trimming and native AOT. Avoid reflection and dynamic activation in production code unless explicitly annotated and publish-tested.
- GitHub CI builds both Debug and Release in `.github/workflows/dotnet-desktop.yml`, but warnings are not configured as errors and formatting is not checked automatically. Run `dotnet build SizeScanner.slnx -c Debug` after substantive changes.

## Import Organization

**Order:**
1. Place `System` namespaces first, sorted before all other directives as required by `.editorconfig`.
2. Place framework and package namespaces next, for example `Avalonia.*`, `CommunityToolkit.Mvvm.*`, and `Microsoft.Extensions.DependencyInjection`.
3. Place project namespaces last, for example `ScannerCore` and `SizeScanner.Avalonia.*`.
4. Put all `using` directives before the namespace declaration; implicit usings are disabled in `Directory.Build.props`, so add every required namespace explicitly.

**Path Aliases:**
- Not applicable. C# references use namespace imports and project references from the `.csproj` files; XAML uses `xmlns:*="using:..."` aliases in `SizeScanner.Avalonia/Views/MainWindow.axaml`.
- Avoid global usings and barrel-style namespace files; neither pattern is established.

## Error Handling

**Patterns:**
- Treat cancellation separately from failure. Re-throw `OperationCanceledException` in engine fallback code (`ScannerCore/ScanEngineSelector.cs`) or catch it with a token filter and restore UI state (`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`).
- Validate public configuration at construction time with BCL guard methods, as `ScanTreeBudget` does with `ArgumentOutOfRangeException.ThrowIf...` in `ScannerCore/ScanTreeBudget.cs`.
- Translate expected boundary failures into domain results where the caller needs a recoverable outcome. `WindowsFileSystemActions.DeleteAsync` returns `DeleteResult` in `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`; native cursor opening returns `null` for inaccessible directories in `ScannerCore/DirectoryScanner.cs`.
- Catch broad exceptions only at deliberate resilience boundaries: elevation detection in `ScannerCore/DriveScanner.cs`, settings loading in `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`, engine fallback in `ScannerCore/ScanEngineSelector.cs`, and user-facing chart scans in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- Preserve cleanup with `using`, `using var`, and `finally`. Examples include pooled-buffer return in `ScannerCore.Tests/DirectoryScannerParsingTests.cs` and scan-state restoration in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`.
- Do not silently swallow unexpected errors in core algorithms. `ScannerCore/BoundedDirectoryWalker.cs` records worker failures and rethrows them after cleanup; `ScannerCore/ScanEngineSelector.cs` only suppresses a failure when another capable engine can be tried.
- Keep failure messages near the boundary that can act on them: debug diagnostics in core, `DeleteResult.Error` in filesystem services, and dialogs in the chart view-model.

## Logging

**Framework:** Debug/trace output; no structured logging framework is configured.

**Patterns:**
- Use `Debug.WriteLine` for low-level scanner diagnostics and fallback information in `ScannerCore/DirectoryScanner.cs` and `ScannerCore/ScanEngineSelector.cs`.
- Avalonia startup routes framework messages to trace with `.LogToTrace()` in `SizeScanner.Avalonia/Program.cs`.
- Surface actionable UI failures through `IDialogService` rather than writing to the console, as in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- Use `ITestOutputHelper` or `Console.WriteLine` only for opt-in performance diagnostics in `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs` and `ScannerCore.Tests/BoundedScanMemoryTests.cs`.
- Do not add ad hoc console output to production services; `ScannerConsole/Program.cs` is the dedicated manual harness.

## Comments

**When to Comment:**
- Explain invariants, platform semantics, concurrency constraints, memory bounds, and non-obvious ownership decisions. Strong examples are the scan contract in `ScannerCore/IScanEngine.cs`, shared scanner race explanation in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, and bounded scope-tree ownership in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- Put short rationale comments beside unusual test setup or math, as in `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs` and `SizeScanner.Avalonia.Tests/SunburstChartBuilderCapTests.cs`.
- Do not narrate routine syntax. Prefer names such as `BuildChartRootWithoutFreeSpace`, `HasUnretainedChildren`, and `InaccessiblePathsTruncated` over explanatory comments.

**JSDoc/TSDoc:**
- Not applicable. Use XML documentation (`///`) for public contracts and subtle internal/test helpers.
- Include `<see cref="..."/>` links when comments name code symbols, as in `ScannerCore/DirectoryWalkEngine.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, and `SizeScanner.Avalonia.Tests/FakeScanService.cs`.
- XML documentation is selective rather than mandatory for every public member; add it when the behavior is not clear from the signature.

## Function Design

**Size:** Keep orchestration methods readable by extracting policy and state transitions. `MainWindowViewModel.ScanTargetAsync` delegates progress, cancellation cleanup, inaccessible-list population, and chart refresh to focused helpers in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`.

**Parameters:**
- Pass cancellation and progress explicitly through scan boundaries, using `CancellationToken` and `IProgress<ScanProgress>` as in `ScannerCore/DriveScanner.cs` and `SizeScanner.Avalonia/Abstractions/IScanService.cs`.
- Use optional parameters only for stable defaults such as `ScanTreeBudget.Default` and `preferAllocatedSize`; use named arguments at call sites when adjacent booleans or optional values would be ambiguous.
- Inject filesystem, dialogs, settings, drive discovery, and scanning through constructor interfaces in view-models. Keep platform IO implementations under `SizeScanner.Avalonia/Services/`.
- Use narrow internal delegates as deterministic test seams for platform policy, such as `Func<string, bool>` in `ScannerCore/DirectoryWalkEngine.cs`.

**Return Values:**
- Return immutable records for snapshots/value results and explicit result types for recoverable failures.
- Use nullable values only when absence is part of the contract: `IDirectoryEntrySource.Open` can return `null` in `ScannerCore/DirectoryEntryCursor.cs`, and `FsItem.Items == null` represents an inaccessible directory in `ScannerCore/FsItem.cs`.
- Do not return partial scan trees after cancellation or worker failure; scanner tests enforce this in `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs` and `ScannerCore.Tests/BoundedDirectoryWalkerTests.cs`.

## Module Design

**Exports:**
- Keep filesystem enumeration, native interop, scan policy, and the `FsItem` model in `ScannerCore/`. UI code consumes the core through project references and does not call Win32 directly.
- Put UI-facing contracts in `SizeScanner.Avalonia/Abstractions/`, Windows and Avalonia implementations in `SizeScanner.Avalonia/Services/`, chart algorithms in `SizeScanner.Avalonia/Charting/`, and state/commands in `SizeScanner.Avalonia/ViewModels/`.
- Keep native declarations nested and internal within the owning core component, as in `ScannerCore/DirectoryScanner.cs` and `ScannerCore/VolumeParallelismPolicy.cs`.
- Use CommunityToolkit source generation via `partial` view-models, `[ObservableProperty]`, and `[RelayCommand]` in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` and `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- Expose selected core internals only to `ScannerCore.Tests` through `InternalsVisibleTo` in `ScannerCore/AssemblyInfo.cs`.
- Keep production code trimming-safe. `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs` provides source-generated JSON metadata; reflection in `SizeScanner.Avalonia.Tests/TestTree.cs` is test-only.

**Barrel Files:**
- Not used. Reference concrete namespaces and files directly; do not introduce aggregate export files.

---

*Convention analysis: 2026-08-06*
