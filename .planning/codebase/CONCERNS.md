# Codebase Concerns

**Analysis Date:** 2026-08-06

## Tech Debt

**Synthetic chart entries are identified by user-visible names:**
- Issue: `ChartNodeRules` treats any item named `[Free space]`, `[Inaccessible]`, `[Filtered]`, or `[Other]` as synthetic, while `SunburstChartBuilder` also chooses colors by those names. A real file or directory can legally use any of these names.
- Files: `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`, `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`, `SizeScanner.Avalonia/Charting/ChartDisplayMetadata.cs`, `ScannerCore/DriveScanMetadata.cs`
- Impact: Real filesystem objects with reserved-looking names receive synthetic colors and filtering rules, lose their context menu, and cannot be scoped even when they are directories. A real `[Free space]` item is also excluded from the chart's used-total calculation.
- Fix approach: Extend `FsItemKind` or add explicit immutable metadata for every synthetic node; base chart policy on kind/reference/position rather than `Name`.

**Mutable scanner state mixes orchestration and scan results:**
- Issue: `DriveScanner` stores target, totals, progress plumbing, inaccessible paths, and drive occupancy on the scanner instance. `GetDisplayThreshold` depends on `_occupied`, which is not reset by `ScanDirectory`, and its zero-threshold directory behavior is codified by tests despite the method having no production callers.
- Files: `ScannerCore/DriveScanner.cs`, `ScannerCore.Tests/DriveScannerTests.cs`, `SizeScanner.Avalonia/Services/ScanService.cs`
- Impact: Reusing a `DriveScanner` for a drive scan followed by a directory scan leaks prior drive occupancy into public progress/threshold APIs. The UI avoids this by replacing the scanner for each root scan, but direct consumers can receive stale values.
- Fix approach: Return immutable scan metadata with the root, move progress state into a per-run context, reset all run-specific fields, and remove or redefine the unused `GetDisplayThreshold` API.

**Settings persistence is synchronous and non-atomic:**
- Issue: Settings are written directly with `File.WriteAllText` from UI-triggered paths; load failures are swallowed and replaced with defaults.
- Files: `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/Views/MainWindow.axaml.cs`
- Impact: A crash, power loss, or concurrent write can truncate the settings file. The next launch silently discards the user's settings, while a save failure during window close can escape through the UI lifecycle.
- Fix approach: Serialize to a sibling temporary file, flush and atomically replace the destination, validate loaded values, and report or trace persistence failures without blocking window close.

**Declared solution platforms do not match project runtime identifiers:**
- Issue: The solution advertises `Any CPU`, `x64`, and `x86`, but every project fixes `RuntimeIdentifier` to `win-x64`, and release automation only publishes `win-x64`.
- Files: `SizeScanner.slnx`, `ScannerCore/ScannerCore.csproj`, `ScannerConsole/ScannerConsole.csproj`, `ScannerCore.Tests/ScannerCore.Tests.csproj`, `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`, `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`, `.github/workflows/release.yml`
- Impact: Selecting x86 or Any CPU does not produce the architecture implied by the solution configuration. Contributors can mistake untested configurations for supported ones.
- Fix approach: Remove unsupported solution platforms or parameterize runtime identifiers and add matching build/publish coverage for every supported architecture.

**Inaccessible-path metadata is not fully surfaced:**
- Issue: The core returns exact `InaccessibleCount` and `InaccessiblePathsTruncated`, but the UI only binds the sampled path collection. Directory scans label inaccessible size as `0 B` even though the size is unknown.
- Files: `ScannerCore/IScanEngine.cs`, `ScannerCore/DriveScanner.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/Views/MainWindow.axaml`
- Impact: Users cannot tell that the list stopped at `ScanTreeBudget.MaxInaccessiblePaths`, and `0 B` can be read as a measured value rather than an unavailable estimate.
- Fix approach: Expose shown/total counts and a truncation indicator in the view-model, and display “unknown” for directory-scan inaccessible size.

## Known Bugs

**Root-scan failures leave the main window busy:**
- Symptoms: `ScanTargetAsync` handles cancellation but not ordinary exceptions. When `_scan.RunAsync` faults, `Chart.IsRootScanInProgress` is reset, but `IsScanning`, status text, and `_scanCts` are not cleaned up; the exception can also escape an async command.
- Files: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/Services/ScanService.cs`, `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`
- Trigger: Scan a path whose enumeration starts successfully and later returns a native failure, or let any scan engine throw a non-cancellation exception.
- Workaround: Restart the application; scope-side scans already contain the missing error-dialog and cleanup pattern in `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.

**Hard links are counted once per directory entry:**
- Symptoms: Drive and directory totals can exceed the unique bytes represented on disk. On a drive scan this can push progress past 100% and force the synthetic inaccessible size to zero.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/BoundedDirectoryWalker.cs`, `ScannerCore/DriveScanner.cs`
- Trigger: Scan an NTFS volume containing multiple hard links to the same file record.
- Workaround: None in the current scanner; interpret totals as directory-entry totals rather than unique file-record usage.

**The `[Inaccessible]` size conflates multiple causes:**
- Symptoms: The drive chart labels `occupied bytes - enumerated allocation bytes` as inaccessible, although the remainder also includes filesystem metadata, reserved space, snapshots, hard-link over/under-counting, and races during the scan.
- Files: `ScannerCore/DriveScanner.cs`, `ScannerCore/DriveScanMetadata.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`
- Trigger: Scan any full drive; the discrepancy is especially visible on volumes with substantial NTFS metadata, reserved storage, or hard links.
- Workaround: Treat the red sector as an unexplained remainder, not a direct sum of the paths in the inaccessible pane.

**Long native paths can be reported as inaccessible:**
- Symptoms: Deep paths may fail at `CreateFile` even when the current user can access them because the P/Invoke path is not converted to extended-length form and the application manifest does not declare `longPathAware`.
- Files: `ScannerCore/DirectoryScanner.cs`, `SizeScanner.Avalonia/app.manifest`
- Trigger: Scan a tree whose resolved path exceeds legacy Windows path limits on a system where long-path behavior is not enabled for this executable.
- Workaround: Start from a deeper subdirectory or use a shorter mount/path.

**Real objects with synthetic names lose normal behavior:**
- Symptoms: A directory named `[Other]` or `[Filtered]` cannot be scoped or deleted from the chart; files named `[Free space]` or `[Inaccessible]` are colored and counted as synthetic entries.
- Files: `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`, `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`, `SizeScanner.Avalonia/Charting/FilterThreshold.cs`
- Trigger: Create a filesystem object whose name equals one of the chart metadata constants and scan its parent.
- Workaround: Rename the object outside SizeScanner.

## Security Considerations

**Offline reparse points are followed without tag or cycle validation:**
- Risk: The scanner skips ordinary reparse points but follows every reparse point that also has `FILE_ATTRIBUTE_OFFLINE`. That attribute is treated as proof of a safe OneDrive placeholder, yet no reparse tag, target, volume identity, or visited file ID is checked. A crafted or unusual offline reparse point can escape the selected subtree or create a traversal cycle, causing unintended disclosure in the chart or denial of service.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/BoundedDirectoryWalker.cs`
- Current mitigation: Reparse points without the offline bit are skipped, retained object count is bounded, and scans run as the invoking user unless the user explicitly relaunches elevated.
- Recommendations: Query and allowlist the intended cloud-placeholder reparse tags, track directory identity to break cycles, and preserve a “do not follow” default for unknown tags.

**Destructive actions run inside the fully elevated process:**
- Risk: After “Relaunch as Administrator,” scanning, Explorer launch, recycle-bin deletion, and permanent recursive deletion all execute with administrator rights. The selected path is derived from a point-in-time scan and is not revalidated for identity or reparse status immediately before deletion.
- Files: `SizeScanner.Avalonia/Services/WindowsElevationService.cs`, `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, `SizeScanner.Avalonia/app.manifest`
- Current mitigation: The default manifest uses `asInvoker`, elevation requires a UAC prompt, synthetic entries suppress their context menus, and permanent deletion requires a confirmation dialog.
- Recommendations: Keep destructive actions unelevated or isolate privileged scanning in a narrow helper; before deletion, re-check type, attributes, and stable file identity, and reject newly introduced reparse points.

**Release executables are not signed:**
- Risk: Users cannot verify an Authenticode publisher for a tool that can relaunch elevated and permanently delete files; replacement or tampering is harder to distinguish from an official release.
- Files: `.github/workflows/release.yml`, `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`, `SizeScanner.Avalonia/app.manifest`
- Current mitigation: GitHub creates a release from a tagged repository revision and publishes a zip through GitHub-hosted infrastructure.
- Recommendations: Sign the executable and release archive, publish checksums or provenance attestations, and verify signatures before creating the release.

**GitHub Actions dependencies use mutable major-version tags:**
- Risk: Build, CodeQL, auto-merge, artifact, and release jobs execute third-party action code referenced by tags such as `@v3`, `@v4`, and `@v7`; a compromised or moved tag changes trusted CI code without a repository diff.
- Files: `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, `.github/workflows/codeql.yml`, `.github/workflows/dependabot-auto-merge.yml`
- Current mitigation: Workflow permissions are scoped per job, Dependabot tracks action updates, and release write permission is confined to the tag workflow.
- Recommendations: Pin every action to a reviewed commit SHA, retain a version comment, and have Dependabot update those pins through reviewed pull requests.

## Performance Bottlenecks

**Parallel root fan-out materializes every child-directory name:**
- Problem: The parallel path reads the entire root directory into `List<string> directoryNames` before any bounded channel fan-out begins.
- Files: `ScannerCore/BoundedDirectoryWalker.cs`
- Cause: `WalkChildrenInParallel` separates enumeration from worker dispatch instead of streaming names directly into the bounded work channel.
- Improvement path: Feed names to the channel while enumerating, retain only channel capacity plus active work, and add a parallel million-directory allocation test.

**Traversal depth is recursive and not bounded by retained depth:**
- Problem: `MaxRetainedDepth` limits the returned object graph, but the scanner still recursively calls `WalkDirectory` for every physical directory level to compute exact totals. A sufficiently deep tree can exhaust the process stack.
- Files: `ScannerCore/BoundedDirectoryWalker.cs`, `ScannerCore/ScanTreeBudget.cs`, `ScannerCore.Tests/BoundedDirectoryWalkerTests.cs`
- Cause: Filesystem traversal and retained-tree construction share the same recursive call stack.
- Improvement path: Use an explicit stack of traversal frames and keep `MaxRetainedDepth` solely as a retention policy.

**Cancellation cannot interrupt an in-flight native directory query:**
- Problem: Cancellation is checked between batches and directories, but `NtQueryDirectoryFile` is invoked synchronously without a cancellable handle operation.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/BoundedDirectoryWalker.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`
- Cause: The native cursor uses a blocking synchronous handle and receives no cancellation signal.
- Improvement path: Use cancellable overlapped/native I/O where practical, close/cancel the handle on cancellation, and document bounded cancellation latency for local and network paths.

**Worker memory scales by one 1 MiB buffer per degree:**
- Problem: The default parallel scan rents one root buffer plus one 1 MiB buffer for each worker. `MaxDegreeOfParallelism` has no upper bound, and channel capacities also multiply the supplied value by two.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/BoundedDirectoryWalker.cs`, `ScannerCore/ScanTreeBudget.cs`
- Cause: Buffer size and worker count are coupled directly to public budget input.
- Improvement path: Cap the supported degree, use a validated capacity calculation, and benchmark smaller native buffers before exposing larger degrees.

**Large recursive deletes cannot be cancelled or coordinated with scans:**
- Problem: Permanent directory deletion runs in a background task with no cancellation token while toolbar scan actions remain enabled because `MainWindowViewModel.IsBusy` excludes `ChartViewModel.IsDeleting`.
- Files: `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/Views/MainWindow.axaml`
- Cause: Deletion status is treated as display state rather than an application-wide operation lock.
- Improvement path: Include deletion in shared busy state, prevent duplicate delete commands, disable scans/navigation during mutation, and support cancellation where the underlying operation permits it.

## Fragile Areas

**Parallel worker failure coordination can hang:**
- Files: `ScannerCore/BoundedDirectoryWalker.cs`
- Why fragile: The supervisor awaits the producer before observing `Task.WhenAll(workers)`. If all workers fault while the bounded work channel is full, the producer can wait forever for a reader, the results writer is never completed, and the fan-in loop cannot finish.
- Safe modification: Link an internal cancellation source to producer/workers, observe worker failure concurrently with production, complete both channels exactly once, and preserve the original exception.
- Test coverage: `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs` covers equivalence, concurrency, and pre-cancelled scans but does not inject worker faults or assert completion under partial/full worker failure.

**Unsafe native buffer parsing trusts ABI and buffer contents:**
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore.Tests/DirectoryScannerParsingTests.cs`, `ScannerCore.Tests/DirectoryEntryCursorTests.cs`
- Why fragile: `ParseBuffer` uses hard-coded offsets and follows `NextEntryOffset` without validating `IO_STATUS_BLOCK.Information`, record bounds, alignment, even UTF-16 byte length, or monotonic progress. A runtime/OS ABI mismatch or malformed native result can read outside the returned data.
- Safe modification: Define the native layout once, parse only the reported byte count, validate every offset and filename span before dereference, and fail the current directory with a diagnostic rather than risking memory corruption.
- Test coverage: Tests exercise valid real-kernel buffers only; there are no malformed/truncated buffer cases or architecture-specific layout tests.

**Filesystem changes during a scan are not snapshot-consistent:**
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/BoundedDirectoryWalker.cs`, `ScannerCore/DriveScanner.cs`
- Why fragile: Enumeration, child recursion, and drive free-space reads happen at different times without VSS or stable file identities. Rename, delete, growth, or replacement can produce inconsistent totals; a mid-stream native cursor failure aborts the whole scan instead of recording one inaccessible directory.
- Safe modification: Define snapshot semantics explicitly, degrade per-directory race failures into recorded partial/unavailable nodes where safe, and optionally add a VSS-backed engine for consistent drive scans.
- Test coverage: No tests mutate a tree during enumeration or force `DirectoryBatchResult.Failed` after successful batches.

**Chart deletion and scanning use separate concurrency guards:**
- Files: `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/Services/ScanService.cs`
- Why fragile: Root and scope scans explicitly avoid racing the shared scanner, but deletion is excluded from `IsBusy`; a scan can observe a half-deleted tree, and multiple delete commands can target stale context state.
- Safe modification: Introduce one operation coordinator for root scan, scope scan, and filesystem mutation, with command `CanExecute` rules and cancellation ownership.
- Test coverage: `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs` checks deletion status and results but not concurrent scan/delete or duplicate delete execution.

**Error reporting is inconsistent and mostly non-durable:**
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/ScanEngineSelector.cs`, `SizeScanner.Avalonia/Program.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`
- Why fragile: Native and engine failures go to `Debug.WriteLine`/trace, scope failures show a dialog, and root failures are unhandled. Release users have no durable diagnostic record.
- Safe modification: Add a small structured logging abstraction with a per-user log, normalize root/scope error handling, and include native status/path context without exposing more filesystem data than necessary.
- Test coverage: Root-scan fault handling and release logging paths are untested.

## Scaling Limits

**Retained scan tree:**
- Current capacity: Defaults retain at most 100,000 nodes, 99 children per retained directory, six retained levels, 10,000 inaccessible path samples, and four top-level workers.
- Files: `ScannerCore/ScanTreeBudget.cs`, `ScannerCore/BoundedChildCollector.cs`, `ScannerCore/BoundedDirectoryWalker.cs`
- Limit: Retained object count is bounded, but total traversal time remains proportional to all reachable entries; parallel root names and traversal call-stack depth are not bounded by the same budget.
- Scaling path: Stream parallel work, use iterative traversal, expose measured budget telemetry, and reject unsafe budget maxima.

**Sunburst layout and rendering:**
- Current capacity: The builder declares 100,000 total segments and 100 segments per sector; the control also caches one geometry and brush per emitted segment.
- Files: `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`, `SizeScanner.Avalonia/Views/SunburstChartControl.cs`, `SizeScanner.Avalonia/Charting/SunburstChart.cs`
- Limit: Layout, sorting, geometry creation, and cache memory scale with emitted segments; there is no runtime timing/memory telemetry to detect UI stalls.
- Scaling path: Keep practical segment budgets substantially below the global cap, benchmark worst-case layouts, and move expensive rebuild work off the UI thread if the cap is raised.

**Inaccessible-path samples:**
- Current capacity: The exact count is a `long`, but only 10,000 paths are retained by default.
- Files: `ScannerCore/ScanTreeBudget.cs`, `ScannerCore/BoundedDirectoryWalker.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`
- Limit: The UI neither states the cap nor exposes the exact total/truncation flag.
- Scaling path: Display sample count versus exact count and allow export/streaming only through an explicitly bounded mechanism.

## Dependencies at Risk

**Native NT directory-information ABI:**
- Risk: Scanning depends on undocumented/low-level `NtQueryDirectoryFile` behavior and hard-coded `FILE_DIRECTORY_INFORMATION` offsets rather than a managed filesystem API or generated interop layout.
- Impact: A platform or architecture change can break parsing in unsafe code; the fixed `win-x64` runtime currently hides cross-architecture issues.
- Migration plan: Centralize generated Windows interop definitions, validate returned lengths, and keep a slower managed enumeration engine available as a tested fallback.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/ScannerCore.csproj`

**Mutable CI action references:**
- Risk: Major-version tags for first- and third-party actions are not immutable, including the release publisher.
- Impact: CI compromise can alter build artifacts or releases while workflows still appear unchanged in the repository.
- Migration plan: Pin reviewed SHAs and use Dependabot to update them.
- Files: `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, `.github/workflows/codeql.yml`, `.github/workflows/dependabot-auto-merge.yml`

**Unpinned transitive restore graph and rolling SDK feature band:**
- Risk: Direct packages are centrally versioned, but no `packages.lock.json` is present and `global.json` permits `latestFeature` roll-forward.
- Impact: Developer, CI, and release restores can use different transitive graphs or SDK feature bands, reducing reproducibility for native AOT output.
- Migration plan: Enable locked restore for CI/releases, commit lock files, and choose a deliberate SDK roll-forward policy.
- Files: `Directory.Packages.props`, `global.json`, `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`

## Missing Critical Features

**Native AOT/trimming validation on pull requests:**
- Problem: The app requires `PublishAot` and `PublishTrimmed`, but normal CI only restores, tests, and runs `dotnet build`. `dotnet publish` runs only after a release tag has already been pushed.
- Blocks: Trimming, native AOT, single-file, and linker regressions can merge undetected and fail only during release creation.
- Files: `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`, `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`

**Test gate in the release workflow:**
- Problem: The tag-triggered release job publishes and creates a release without running either test project; the branch workflow's filters do not make tag builds a test gate.
- Blocks: A tag on an untested or divergent commit can publish a release even when tests fail.
- Files: `.github/workflows/release.yml`, `.github/workflows/dotnet-desktop.yml`, `ScannerCore.Tests/ScannerCore.Tests.csproj`, `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`

**Signed release and provenance:**
- Problem: Release packaging has no Authenticode signing, checksum publication, SBOM, or build-provenance attestation.
- Blocks: Strong publisher verification and tamper-evident distribution for an elevation-capable destructive utility.
- Files: `.github/workflows/release.yml`, `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`

**GitLab pipeline:**
- Problem: No canonical `.gitlab-ci.yml` is present, so GitLab pushes and merge requests have no repository-defined build, test, security, or release gate.
- Blocks: Equivalent validation when GitLab is used as the project host.
- Files: `SizeScanner.slnx`, `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`

## Test Coverage Gaps

**Parallel fault, memory, and cancellation behavior:**
- What's not tested: Worker exceptions with full channels, producer/consumer shutdown, wide roots containing millions of directories, cancellation during a native call, and extreme `MaxDegreeOfParallelism` values.
- Files: `ScannerCore/BoundedDirectoryWalker.cs`, `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`, `ScannerCore.Tests/BoundedScanMemoryTests.cs`
- Risk: Deadlocks and transient-memory regressions can pass the current bounded-node and small parallel-tree tests.
- Priority: High

**Filesystem identity edge cases:**
- What's not tested: Hard links, offline reparse tags, reparse cycles, mount points, long paths, and tree mutation during enumeration.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/BoundedDirectoryWalker.cs`, `ScannerCore.Tests/DirectoryScannerParsingTests.cs`, `ScannerCore.Tests/DirectoryEntryCursorTests.cs`
- Risk: Incorrect totals, traversal outside the selected tree, unresponsive cancellation, or full-scan failure can ship unnoticed.
- Priority: High

**Unsafe parser validation:**
- What's not tested: Truncated records, invalid offsets, odd filename lengths, incorrect returned byte counts, and x86/ARM64 layout.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore.Tests/DirectoryScannerParsingTests.cs`
- Risk: Native-layout regressions affect unsafe memory reads rather than producing a controlled parse failure.
- Priority: High

**Root scan failure cleanup:**
- What's not tested: A non-cancellation exception from `IScanService.RunAsync`, dialog/status behavior, command re-enablement, and cancellation-source disposal.
- Files: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`, `SizeScanner.Avalonia.Tests/FakeScanService.cs`
- Risk: The UI remains permanently busy or an async command exception terminates the application.
- Priority: High

**Destructive operation boundaries:**
- What's not tested: Recycle-bin directory deletion, permanent recursive directory deletion, reparse replacement before delete, elevated behavior, Explorer launch failures, duplicate delete commands, and scan/delete concurrency.
- Files: `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`, `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`
- Risk: Stale-path and concurrency defects affect destructive filesystem operations.
- Priority: High

**Synthetic-name collisions and inaccessible truncation UI:**
- What's not tested: Real objects named like chart metadata, display of `InaccessibleCount`, `InaccessiblePathsTruncated`, and unknown directory-scan inaccessible size.
- Files: `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`, `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`
- Risk: Valid objects become non-interactive and users receive incomplete or misleading scan information.
- Priority: Medium

**Performance and publish gates:**
- What's not tested: The ten-million-entry memory test and real-volume parallel speed test are opt-in, no coverage threshold is enforced, and pull requests do not native-publish the application. The current Release suite passes 136 tests with one performance test skipped, while four xUnit cancellation-token analyzer warnings remain in UI tests.
- Files: `ScannerCore.Tests/BoundedScanMemoryTests.cs`, `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`, `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`, `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`, `.github/workflows/dotnet-desktop.yml`
- Risk: Performance, native AOT, trimming, and cancellation-responsiveness regressions are not release-blocking.
- Priority: Medium

---

*Concerns audit: 2026-08-06*
