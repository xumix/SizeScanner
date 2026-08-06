# Testing Patterns

**Analysis Date:** 2026-08-06

## Test Framework

**Runner:**
- xUnit v3 `3.2.2` with `xunit.runner.visualstudio` `3.1.5`.
- Microsoft.NET.Test.Sdk `18.8.1` supplies `dotnet test` integration.
- Package versions are centralized in `Directory.Packages.props`; test project references are in `ScannerCore.Tests/ScannerCore.Tests.csproj` and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.
- Both test projects target `net10.0-windows`, use the `win-x64` runtime identifier, and are non-packable executable test projects.
- Config: `Directory.Build.props`, `Directory.Packages.props`, `ScannerCore.Tests/ScannerCore.Tests.csproj`, and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`. No `.runsettings` file or assembly-level xUnit configuration is present.

**Assertion Library:**
- Use xUnit's built-in `Assert` API. No FluentAssertions, Shouldly, Moq, NSubstitute, FakeItEasy, or AutoFixture dependency is present.
- Prefer the most specific assertion: `Assert.Single`, `Assert.Empty`, `Assert.Same`, `Assert.IsType`, `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Throws`, and `Assert.ThrowsAnyAsync`.
- For floating-point chart geometry, use xUnit's precision overload, as in `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs` and `SizeScanner.Avalonia.Tests/SliceColorPaletteTests.cs`.

**Run Commands:**
```powershell
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release
dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release
dotnet test SizeScanner.slnx -c Release
dotnet watch --project ScannerCore.Tests/ScannerCore.Tests.csproj test
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
```
- Use the two project-specific commands for the same separation enforced in `.github/workflows/dotnet-desktop.yml`.
- Watch mode is not repository-configured; the `dotnet watch` command is an optional local workflow.
- Tests require Windows because the projects target `net10.0-windows` and core integration tests exercise native directory enumeration.

## Test File Organization

**Location:**
- Keep core tests in the separate `ScannerCore.Tests/` project and Avalonia/application tests in `SizeScanner.Avalonia.Tests/`.
- Mirror the production subject by filename rather than co-locating tests. `ScannerCore/DriveScanner.cs` maps to `ScannerCore.Tests/DriveScannerTests.cs`; `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs` maps to `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- Put cross-test helpers at the test-project root: `ScannerCore.Tests/TemporaryDirectory.cs`, `ScannerCore.Tests/SyntheticDirectoryEntrySource.cs`, `ScannerCore.Tests/RecordingEntrySink.cs`, `SizeScanner.Avalonia.Tests/TempDir.cs`, `SizeScanner.Avalonia.Tests/TestTree.cs`, and `SizeScanner.Avalonia.Tests/FakeScanService.cs`.
- Keep generated `bin/`, `obj/`, `TestResults/`, and nested `.worktrees/` content out of test-source analysis and source control; `.gitignore` excludes each category.

**Naming:**
- Name test files and classes `<Subject>Tests`.
- Name test methods `Subject_or_scenario_expected_behavior`, using underscores between clauses: `RunScopeAsync_builds_tree_without_mutating_root_scan_state` in `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`.
- Name test doubles for their behavior (`FakeEngine`, `NoopFs`, `FailingFs`, `PendingFs`) and factories for the object they create (`CreateVm`, `SampleDriveRoot`).

**Structure:**
```text
ScannerCore.Tests/
├── <CoreSubject>Tests.cs
├── TemporaryDirectory.cs
├── SyntheticDirectoryEntrySource.cs
└── RecordingEntrySink.cs

SizeScanner.Avalonia.Tests/
├── <ApplicationSubject>Tests.cs
├── FakeScanService.cs
├── TestTree.cs
└── TempDir.cs
```

## Test Structure

**Suite Organization:**
```csharp
[Fact]
public async Task DeleteAsync_permanent_removes_file_and_reports_success()
{
    using var dir = new TempDir();
    var file = dir.CreateFile("doomed.bin", 16);
    var actions = new WindowsFileSystemActions();

    var result = await actions.DeleteAsync(file, permanent: true);

    Assert.True(result.Success);
    Assert.Null(result.Error);
    Assert.False(File.Exists(file));
}
```
- This arrange/act/assert pattern is used in `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`.

**Patterns:**
- Use one behavior per `[Fact]`, with blank lines separating arrange, act, and assert.
- Use `[Theory]` and `[InlineData]` when the same contract must hold across a small input matrix, as in `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`.
- Build only the state needed by the test. Pure chart tests construct small `FsItem` trees through `TestTree` in `SizeScanner.Avalonia.Tests/TestTree.cs`.
- Assert externally visible state and collaborator calls. `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs` checks layout, scope state, command behavior, and `FakeScanService.RootCalls`/`ScopeCalls`.
- Test public behavior by default. Core-only seams are available to the core test assembly through `[assembly: InternalsVisibleTo("ScannerCore.Tests")]` in `ScannerCore/AssemblyInfo.cs`.
- Dispose all temporary directories, cancellation sources, and cursors with `using var`. Return pooled buffers in `finally`, as in `ScannerCore.Tests/DirectoryScannerParsingTests.cs`.
- Use named arguments for boolean modes and budgets so test intent remains clear.

## Mocking

**Framework:** Manual fakes, stubs, no-op implementations, synthetic sources, and real temporary filesystem fixtures; no mocking library.

**Patterns:**
```csharp
internal sealed class FakeScanService : IScanService
{
    public List<(string Target, bool IsDrive)> RootCalls { get; } = [];
    public Func<string, bool, FsItem>? RootResult { get; set; }
    public TaskCompletionSource<FsItem>? PendingRoot { get; set; }

    public Task<FsItem> RunAsync(
        string target,
        bool isDrive,
        CancellationToken cancellationToken,
        IProgress<ScanProgress> progress,
        ScanTreeBudget? budget = null)
    {
        RootCalls.Add((target, isDrive));
        if (PendingRoot is not null)
            return PendingRoot.Task;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RootResult!(target, isDrive));
    }
}
```
- The full configurable recorder is in `SizeScanner.Avalonia.Tests/FakeScanService.cs`.
- Keep tiny single-suite doubles nested in the test class, as `FakeEngine` is in `ScannerCore.Tests/ScanEngineSelectorTests.cs` and `NoopFs`/`PendingFs` are in `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- Implement core streaming contracts with deterministic in-memory sources and cursors in `ScannerCore.Tests/SyntheticDirectoryEntrySource.cs`; capture span-backed callback values immediately in `ScannerCore.Tests/RecordingEntrySink.cs`.
- Inject deterministic policy delegates instead of mocking static platform APIs, as `DirectoryWalkEngine(Func<string, bool>)` is used in `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`.

**What to Mock:**
- Mock UI boundaries defined under `SizeScanner.Avalonia/Abstractions/`: scanning, dialogs, settings, drive discovery, folder picking, elevation, and filesystem actions.
- Fake native directory input with `IDirectoryEntrySource`/`IDirectoryEntryCursor` when testing walker budgets, inaccessible handling, memory bounds, or parallel scheduling independently of disk layout.
- Record calls and configurable results when interaction order or root-versus-scope state is part of the contract.
- Control incomplete async operations with `TaskCompletionSource`, preferably using `TaskCreationOptions.RunContinuationsAsynchronously` as `PendingFs` does in `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.

**What NOT to Mock:**
- Do not mock pure chart algorithms or `FsItem`; construct real model trees with `SizeScanner.Avalonia.Tests/TestTree.cs`.
- Do not mock JSON serialization; round-trip a real file under a unique temporary directory in `SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs`.
- Do not mock filesystem enumeration when validating native cursor parsing, logical/allocation size semantics, parent links, or service integration. Use `ScannerCore.Tests/TemporaryDirectory.cs` or `SizeScanner.Avalonia.Tests/TempDir.cs`.
- Do not run destructive tests against user data. `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs` tests permanent deletion only inside `TempDir` and does not automate recycle-bin behavior.

## Fixtures and Factories

**Test Data:**
```csharp
var root = TestTree.Dir("C:\\",
    TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
    TestTree.File(DriveScanMetadata.InaccessibleName, 0),
    TestTree.Dir("Windows",
        TestTree.File("kernel.sys", 300)));
```
- `SizeScanner.Avalonia.Tests/TestTree.cs` computes directory sizes, attaches children, and establishes parent links.
- `ScannerCore.Tests/SyntheticDirectoryEntrySource.cs` emits wide synthetic directories in bounded batches without allocating a million `FsItem` inputs.
- `TemporaryDirectory.CreateFile` and `TempDir.CreateFile` create unique real filesystem fixtures and delete them best-effort during disposal.

**Location:**
- Core filesystem fixture: `ScannerCore.Tests/TemporaryDirectory.cs`.
- Core streaming source and factory: `ScannerCore.Tests/SyntheticDirectoryEntrySource.cs`.
- Core callback recorder: `ScannerCore.Tests/RecordingEntrySink.cs`.
- Avalonia tree factory: `SizeScanner.Avalonia.Tests/TestTree.cs`.
- Avalonia filesystem fixture: `SizeScanner.Avalonia.Tests/TempDir.cs`.
- Avalonia scan fake: `SizeScanner.Avalonia.Tests/FakeScanService.cs`.
- Prefer a shared helper when multiple test classes need identical semantics; keep one-off fakes private to the suite.

## Coverage

**Requirements:** No numeric line, branch, or method coverage threshold is enforced.

**View Coverage:**
```powershell
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
```
- `coverlet.collector` `10.0.1` is private to both test projects through their `.csproj` files.
- `.github/workflows/dotnet-desktop.yml` collects XPlat coverage for both test projects in Debug and Release, writes TRX and Cobertura files under `TestResults/`, and uploads test/coverage artifacts from the Release matrix job.
- Coverage reports are artifacts only; the workflow does not merge reports, publish a summary, compare against a baseline, or fail on a threshold.
- `.github/workflows/release.yml` publishes native-AOT artifacts on `v*` tags but does not run tests itself. Release confidence therefore depends on the branch workflow having completed before tagging.
- `TestResults/` is ignored through `.gitignore`; do not commit local reports.

## Test Types

**Unit Tests:**
- Core model, budget, metadata, formatting, collector, engine selection, and pure policy tests live in `ScannerCore.Tests/FsItemTests.cs`, `ScannerCore.Tests/ScanTreeBudgetTests.cs`, `ScannerCore.Tests/DriveScanMetadataTests.cs`, `ScannerCore.Tests/HumanizeTests.cs`, `ScannerCore.Tests/BoundedChildCollectorTests.cs`, and `ScannerCore.Tests/ScanEngineSelectorTests.cs`.
- Chart builder, hit-testing, filtering, palette, tooltip, and view-model policy tests live under `SizeScanner.Avalonia.Tests/`.
- Use exact domain assertions for sizes, node identity, parent links, synthetic metadata, segment order, sweep angles, and call records.

**Integration Tests:**
- Native directory cursor and parser integration tests use temporary real directories in `ScannerCore.Tests/DirectoryEntryCursorTests.cs` and `ScannerCore.Tests/DirectoryScannerParsingTests.cs`.
- End-to-end core scan composition is exercised by `ScannerCore.Tests/DriveScannerTests.cs`, `ScannerCore.Tests/DirectoryWalkEngineTests.cs`, and `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`.
- Service/filesystem integration is exercised by `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`, `SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs`, and `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`.
- Platform-sensitive tests must stay Windows-safe and self-contained. `ScannerCore.Tests/VolumeParallelismPolicyTests.cs` avoids asserting SSD/HDD hardware details and only checks stable policy outcomes.

**E2E Tests:**
- No desktop automation, screenshot comparison, rendered visual regression, or packaged-application E2E framework is used.
- `SizeScanner.Avalonia.Tests/ViewLocatorTests.cs` constructs Avalonia view objects without launching the desktop lifetime; `SizeScanner.Avalonia.Tests/SmokeTests.cs` validates test-tree plumbing, not a running application.
- `ScannerConsole/Program.cs` is a manual scan/performance harness, not an automated test suite.

**Performance Tests:**
- Mark opt-in performance checks with `[Trait("Category", "Performance")]`.
- Gate expensive or machine-dependent runs with `SIZESCANNER_RUN_PERF_TESTS=1`. `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs` uses `Assert.SkipUnless` for environment, path, and storage-class preconditions.
- `ScannerCore.Tests/BoundedScanMemoryTests.cs` keeps deterministic million-entry bounded-memory assertions in the normal suite and gates only the ten-million-entry diagnostic case.
- Emit measurement detail with `ITestOutputHelper` or test console output, never as production logging.

## Common Patterns

**Async Testing:**
```csharp
var scopeTask = vm.TryScopeAtAsync(windows);

Assert.True(vm.IsScopeScanning);
Assert.Same(layoutBefore, vm.Layout);

pending.SetResult(scannedTree);
Assert.True(await scopeTask);
Assert.False(vm.IsScopeScanning);
```
- This controlled in-flight pattern is used in `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs` and `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`.
- Await asynchronous APIs directly; do not block with `.Result` or `.Wait()`.
- For cancellation, cancel a `CancellationTokenSource`, complete the controlled pending task as canceled when applicable, await the operation, and assert both return/exception semantics and restored state.
- When waiting for a test double to signal that an operation started, use an explicit timeout plus `TestContext.Current.CancellationToken`, as in `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- Use `Assert.ThrowsAnyAsync<OperationCanceledException>` for service cancellation contracts, as in `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`.

**Error Testing:**
```csharp
Assert.Throws<OperationCanceledException>(() =>
    engine.Scan(
        temp.Path,
        isDriveScan: false,
        cts.Token,
        onProgress: null,
        ScanTreeBudget.Default));
```
- Assert exact exception types for synchronous validation/cancellation in `ScannerCore.Tests/ScanTreeBudgetTests.cs` and `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`.
- For recoverable UI/service failures, configure a failing fake and assert that state remains unchanged, as `FailingFs` is used in `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- For corrupt or missing persistence input, exercise the real boundary and assert defaults rather than the internal catch block, as in `SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs`.
- For missing filesystem objects, assert structured failure (`DeleteResult`) or nullable cursor contracts instead of expecting exceptions, as in `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs` and `ScannerCore.Tests/DirectoryEntryCursorTests.cs`.

**Concurrency and Resource Bounds:**
- Compare sequential and parallel results for total, ordering, parent links, and retained-node limits in `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`.
- Use a tracking source around the real cursor to observe maximum worker concurrency; keep timing sleeps narrowly scoped and explain why they are needed.
- Validate both exact totals and bounded retained structures with synthetic million-entry inputs in `ScannerCore.Tests/BoundedDirectoryWalkerTests.cs` and `ScannerCore.Tests/BoundedScanMemoryTests.cs`.
- Use unique GUID-based temporary directory names so xUnit's default class-level parallelism does not collide. No repository-wide collection disables parallel execution.

## CI Practices

- `.github/workflows/dotnet-desktop.yml` runs on pushes and pull requests to `main` or `master` using `windows-latest`.
- The workflow tests both `ScannerCore.Tests/ScannerCore.Tests.csproj` and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj` in a Debug/Release matrix before building `SizeScanner.slnx`.
- Each CI test invocation writes a distinct TRX filename and collects XPlat coverage into `TestResults/`.
- Release-matrix test and coverage artifacts are uploaded even when tests fail (`always()`); Release build artifacts are uploaded only on success.
- `.github/workflows/release.yml` performs restore, native-AOT publish, packaging, and GitHub release creation for `v*` tags. It contains no independent test step.
- No GitLab pipeline file is present in the canonical repository. Do not infer CI behavior from nested `.worktrees/` copies.

---

*Testing analysis: 2026-08-06*
