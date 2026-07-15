# Testing Patterns

**Analysis Date:** 2026-07-16

## Test Framework

**Runner:**
- xUnit v3 3.2.2 runs both test projects; package versions are centralized in `/Directory.Packages.props`.
- Microsoft.NET.Test.Sdk 18.6.0 and xunit.runner.visualstudio 3.1.5 provide `dotnet test` and IDE discovery through `ScannerCore.Tests/ScannerCore.Tests.csproj` and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.
- Both test projects target `net10.0-windows`, select `win-x64`, use xUnit v3's executable test-project model, and are non-packable in `ScannerCore.Tests/ScannerCore.Tests.csproj` and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.
- The .NET 10.0.100 SDK baseline is pinned with latest-feature roll-forward in `/global.json`.

**Assertion Library:**
- Use xUnit's `Assert` APIs directly; no FluentAssertions package is listed in `/Directory.Packages.props`.
- Prefer specific assertions such as `Assert.Equal`, `Assert.Single`, `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Same`, and `Assert.IsType`, as demonstrated in `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs` and `SizeScanner.Avalonia.Tests/ViewLocatorTests.cs`.

**Run Commands:**
```powershell
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release
dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~DirectoryWalkEngineTests"
dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --collect:"XPlat Code Coverage"
```
- The two project-level Release commands are the canonical local suite commands documented in `/AGENTS.md`.
- Filter by fully qualified name for focused runs, following the command pattern documented in `docs/superpowers/plans/2026-06-17-scanner-optimization-phases-1-4.md`.
- No repository-specific watch-mode command or test run-settings file is configured in `/SizeScanner.slnx` or either test project.

## Test File Organization

**Location:**
- Core tests live in the separate `ScannerCore.Tests/` project and reference `ScannerCore/` through `ScannerCore.Tests/ScannerCore.Tests.csproj`.
- UI, chart, view-model, and service tests live in `SizeScanner.Avalonia.Tests/` and reference both production projects through `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.
- Test helpers are project-local: `ScannerCore.Tests/TemporaryDirectory.cs`, `SizeScanner.Avalonia.Tests/TempDir.cs`, and `SizeScanner.Avalonia.Tests/TestTree.cs`.

**Naming:**
- Name files and classes `{Subject}Tests`, for example `ScannerCore.Tests/ScanEngineSelectorTests.cs` and `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- Name methods as `{Member}_{expected_behavior}` with underscores, for example `RunAsync_honors_cancellation` in `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`.
- All detected tests use `[Fact]`; no `[Theory]`, `[InlineData]`, collection fixture, or assembly fixture pattern appears in `ScannerCore.Tests/` or `SizeScanner.Avalonia.Tests/`.

**Structure:**
```text
ScannerCore.Tests/
├── *Tests.cs                  # Core unit and Windows filesystem integration tests
└── TemporaryDirectory.cs     # Disposable filesystem fixture

SizeScanner.Avalonia.Tests/
├── *Tests.cs                  # Chart, service, view-model, and light view tests
├── TempDir.cs                 # Disposable filesystem fixture
└── TestTree.cs                # FsItem tree factory
```

## Test Structure

**Suite Organization:**
```csharp
[Fact]
public void Scan_builds_tree_with_sizes_parents_and_total()
{
    using var temp = new TemporaryDirectory();
    temp.CreateFile("a.txt", 100);

    var result = new DirectoryWalkEngine().Scan(
        temp.Path, isDriveScan: false, CancellationToken.None, onProgress: null);

    Assert.Equal(100, result.Total);
    Assert.Same(result.Root, result.Root.Items![0].Parent);
}
```
- This arrange/act/assert shape, separated by blank lines rather than labels, is used in `ScannerCore.Tests/DirectoryWalkEngineTests.cs`.

**Patterns:**
- Construct the subject inside each test rather than sharing mutable fixtures; examples include `ScannerCore.Tests/DriveScannerTests.cs` and `SizeScanner.Avalonia.Tests/FilterThresholdTests.cs`.
- Build only the minimum domain tree needed through `TestTree.Dir` and `TestTree.File` from `SizeScanner.Avalonia.Tests/TestTree.cs`.
- Assert both observable results and important invariants such as parent identity, aggregate size, ring index, and angular totals in `ScannerCore.Tests/DirectoryWalkEngineTests.cs` and `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`.
- Use predicate overloads of `Assert.Contains`, `Assert.DoesNotContain`, and `Assert.Single` for chart segments in `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`.
- Include diagnostic values in performance or cap assertions, as in `SizeScanner.Avalonia.Tests/SunburstChartBuilderCapTests.cs` and `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`.
- Test public behavior by default; core-only seams are accessible through `[assembly: InternalsVisibleTo("ScannerCore.Tests")]` in `ScannerCore/AssemblyInfo.cs`.

## Mocking

**Framework:** Hand-written fakes and no-op implementations; no Moq, NSubstitute, FakeItEasy, or other mocking library is configured in `/Directory.Packages.props`.

**Patterns:**
```csharp
private sealed class FakeSettings : ISettingsStore
{
    public UserSettings Loaded { get; set; } = new();
    public UserSettings Saved { get; private set; } = new();
    public UserSettings Load() => Loaded;
    public void Save(UserSettings settings) => Saved = settings;
}
```
- Keep small fakes nested in the owning test class, as in `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs` and `ScannerCore.Tests/ScanEngineSelectorTests.cs`.
- Expose captured arguments and call counts directly from fakes, such as `LastTarget`, `IsDriveScan`, and `ScanCalls` in `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs` and `ScannerCore.Tests/ScanEngineSelectorTests.cs`.
- Use behavior-specific implementations (`NoopFs`, `FailingFs`, and `PendingFs`) instead of configurable general mocks in `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- Inject a small delegate when a full interface fake is unnecessary; `DirectoryWalkEngine(Func<string, bool>)` is exercised in `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`.

**What to Mock:**
- Replace UI dialogs, drive discovery, elevation, folder selection, settings, and destructive filesystem commands behind interfaces from `SizeScanner.Avalonia/Abstractions/` when testing view models, as shown in `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`.
- Replace scan engines through `ScannerCore/IScanEngine.cs` when verifying selection and fallback behavior in `ScannerCore.Tests/ScanEngineSelectorTests.cs`.
- Control asynchronous boundaries with `TaskCompletionSource` configured with `RunContinuationsAsynchronously`, as in `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.

**What NOT to Mock:**
- Use the real native directory scanner against disposable temp trees for enumeration, sizing, concurrency, and walk behavior in `ScannerCore.Tests/DirectoryScannerParsingTests.cs` and `ScannerCore.Tests/DirectoryWalkEngineTests.cs`.
- Use real chart builders and `FsItem` trees for geometry and filtering behavior in `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`.
- Use the real JSON serializer and filesystem for settings round trips in `SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs`.
- Exercise permanent deletion with a temporary file in `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`; do not exercise recycle-bin UI behavior in the automated suite.

## Fixtures and Factories

**Test Data:**
```csharp
var root = TestTree.Dir("C:\\",
    TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
    TestTree.Dir("Windows",
        TestTree.File("kernel.sys", 300)));
```
- `SizeScanner.Avalonia.Tests/TestTree.cs` sums child sizes and attaches parent pointers so chart and view-model tests use valid trees.
- `ScannerCore.Tests/TemporaryDirectory.cs` and `SizeScanner.Avalonia.Tests/TempDir.cs` create unique directories below the OS temp path and delete them best-effort in `Dispose`.
- Use `using var` for temp directories and `CancellationTokenSource` instances, as in `ScannerCore.Tests/DriveScannerTests.cs` and `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`.

**Location:**
- Keep core filesystem fixtures in `ScannerCore.Tests/TemporaryDirectory.cs`.
- Keep Avalonia-side fixtures and tree factories in `SizeScanner.Avalonia.Tests/TempDir.cs` and `SizeScanner.Avalonia.Tests/TestTree.cs`.
- No external fixture files, snapshots, golden images, or test-data directory are used by the test projects in `ScannerCore.Tests/` and `SizeScanner.Avalonia.Tests/`.

## Coverage

**Requirements:** Coverage is collected and archived, but no minimum percentage or quality gate is configured in `.github/workflows/dotnet-desktop.yml`.

**Collector:**
- Both test projects reference coverlet.collector 10.0.1 as a private asset in `ScannerCore.Tests/ScannerCore.Tests.csproj` and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`; the version is centralized in `/Directory.Packages.props`.
- GitHub CI passes `--collect:"XPlat Code Coverage"` to both test projects in `.github/workflows/dotnet-desktop.yml`.
- Release-matrix coverage files matching `TestResults/**/*.cobertura.xml` are uploaded as the `coverage` artifact in `.github/workflows/dotnet-desktop.yml`.
- No report-merging step, HTML report generation, exclusion policy, or checked-in `.runsettings` coverage configuration is present in `/SizeScanner.slnx` or `.github/workflows/dotnet-desktop.yml`.

**View Coverage:**
```powershell
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
```
- Inspect the generated Cobertura XML under `TestResults/`; no repository command renders a local HTML report in `.github/workflows/dotnet-desktop.yml`.

## Test Types

**Unit Tests:**
- Pure formatting, metadata, policy edge cases, and model contracts are tested in `ScannerCore.Tests/HumanizeTests.cs`, `ScannerCore.Tests/DriveScanMetadataTests.cs`, and `ScannerCore.Tests/FsItemTests.cs`.
- Chart construction, cap behavior, hit-testing, colors, filtering, and tooltips are tested without a running application in `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`, `SizeScanner.Avalonia.Tests/SunburstChartBuilderCapTests.cs`, and `SizeScanner.Avalonia.Tests/SunburstHitTestTests.cs`.
- View-model tests inject fakes and execute generated commands in `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs` and `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.

**Integration Tests:**
- Windows native enumeration and real temp filesystem behavior are exercised in `ScannerCore.Tests/DirectoryScannerParsingTests.cs`, `ScannerCore.Tests/DirectoryWalkEngineTests.cs`, and `ScannerCore.Tests/DriveScannerTests.cs`.
- Concurrency and storage-device policy behavior are covered in `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs` and `ScannerCore.Tests/VolumeParallelismPolicyTests.cs`.
- Service-to-core integration and cancellation are exercised in `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`.
- JSON file persistence and permanent filesystem deletion are exercised in `SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs` and `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`.
- Lightweight Avalonia object construction is covered in `SizeScanner.Avalonia.Tests/ViewLocatorTests.cs`; no headless UI host or rendered-window fixture is configured in `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.

**Performance Tests:**
- `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs` is tagged with `[Trait("Category", "Performance")]` and skips unless `SIZESCANNER_RUN_PERF_TESTS=1`, `C:\` exists, and the volume policy identifies SSD-class storage.
- The performance test alternates sequential/parallel order, takes two samples, compares medians, and writes diagnostics through `ITestOutputHelper` in `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`.
- Default developer and CI test runs skip the drive-wide performance test through `Assert.SkipUnless` in `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`.

**E2E Tests:**
- No application-process, window-driving, screenshot, or installer E2E framework is configured in `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.
- `SizeScanner.Avalonia.Tests/SmokeTests.cs` validates the shared tree helper, not an end-to-end application launch.

## Common Patterns

**Async Testing:**
```csharp
var deleteTask = vm.DeleteCommand.ExecuteAsync(null);
await fileSystem.Started.Task.WaitAsync(
    TimeSpan.FromSeconds(5),
    TestContext.Current.CancellationToken);

Assert.True(vm.IsDeleting);
fileSystem.Completion.SetResult(new DeleteResult(true, null));
await deleteTask;
```
- Use xUnit v3's `TestContext.Current.CancellationToken` and an explicit timeout when waiting for a controlled asynchronous state, as in `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`.
- Return `Task` from async test methods and await production operations directly in `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs` and `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`.

**Error Testing:**
```csharp
using var cts = new CancellationTokenSource();
cts.Cancel();

await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
    service.RunAsync(dir.Path, isDrive: false, cts.Token, progress));
```
- Assert cancellation by exception at the async service boundary in `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`.
- Assert result objects for recoverable filesystem failures in `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`.
- Verify fallback behavior by making a fake engine throw in `ScannerCore.Tests/ScanEngineSelectorTests.cs`.
- Verify malformed configuration falls back to defaults in `SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs`.

## CI Test Execution

- GitHub Actions runs both test projects on `windows-latest` for Debug and Release configurations in `.github/workflows/dotnet-desktop.yml`.
- Each CI test command writes a distinct TRX file and collects XPlat coverage into `TestResults/` in `.github/workflows/dotnet-desktop.yml`.
- Test result and coverage artifacts upload only from the Release matrix entry and use `always()` so failures still preserve diagnostics in `.github/workflows/dotnet-desktop.yml`.
- The workflow restores and builds `SizeScanner.slnx` after the test steps, using the SDK pinned by `/global.json` and package cache inputs from all project files plus `/Directory.Packages.props`.
- CodeQL performs a separate manual Release build on `windows-latest` but does not run tests in `.github/workflows/codeql.yml`.
- Release publishing restores and publishes the application but does not rerun tests in `.github/workflows/release.yml`.
- No tracked `.gitlab-ci.yml` is present at the repository root; the active test pipeline definition detected in this repository is `.github/workflows/dotnet-desktop.yml`.

---

*Testing analysis: 2026-07-16*
