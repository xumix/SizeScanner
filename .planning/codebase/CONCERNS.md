# Codebase Concerns

**Analysis Date:** 2026-07-16

## Audit Baseline

- Release build succeeds for `SizeScanner.slnx`; the core suite reports 31 passed and 1 skipped test from `ScannerCore.Tests/ScannerCore.Tests.csproj`, and the UI suite reports 64 passed tests from `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.
- Native AOT publication succeeds for `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`. Pull-request CI only builds the solution; native AOT publication is exercised by tag releases in `.github/workflows/release.yml`.
- The skipped test is the only real-volume speed comparison and requires `SIZESCANNER_RUN_PERF_TESTS=1` in `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`.

## Priority Summary

1. **P0 — Make scan lifecycle single-flight and exception-safe.** Gate commands in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, capture each scanner locally in `SizeScanner.Avalonia/Services/ScanService.cs`, and restore UI state in a `finally` block.
2. **P0 — Bound parallelism globally.** `ScannerCore/DirectoryWalkEngine.cs` recursively creates nested `Parallel.ForEach` loops despite the top-level-only contract in `AGENTS.md` and `docs/superpowers/plans/2026-06-17-scanner-optimization-phases-1-4.md`.
3. **P0 — Harden native enumeration.** Validate every entry against `IO_STATUS_BLOCK.Information`, distinguish partial query failures from successful completion, and surface native errors from `ScannerCore/DirectoryScanner.cs`.
4. **P1 — Correct disk accounting.** Track file identity to avoid hard-link double counting and stop presenting all `occupied - scanned` bytes as inaccessible in `ScannerCore/DriveScanner.cs`.
5. **P1 — Make chart preprocessing scale with the render budget.** Avoid whole-directory candidate lists, full sorts, and a dictionary entry per node in `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`.
6. **P1 — Replace name-based synthetic identity.** Real filesystem entries named `[Free space]`, `[Inaccessible]`, `[Filtered]`, or `[Other]` are misclassified by `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`.
7. **P1 — Harden destructive actions.** Revalidate the target against the scan root immediately before deletion and remove stale context-menu targeting in `SizeScanner.Avalonia/Views/ChartView.axaml.cs`.
8. **P1 — Add long-path and deep-tree support.** Native paths are neither extended with `\\?\` in `ScannerCore/DirectoryScanner.cs` nor opted into with `longPathAware` in `SizeScanner.Avalonia/app.manifest`; recursive walkers also retain stack-overflow risk.
9. **P2 — Enforce release security and quality gates.** Add code signing, immutable action pins, an AOT publish check, analyzer enforcement, and coverage thresholds in `.github/workflows/` and `Directory.Build.props`.
10. **P2 — Resolve unfinished fast-path work and documentation drift.** The MFT design in `docs/superpowers/specs/2026-06-17-mft-scan-engine-design.md` has no implementation, and the GitLab pipeline claimed by `AGENTS.md` is absent.

## Tech Debt

**Scan lifecycle ownership:**
- Issue: Scan commands are disabled only at the control layer in `SizeScanner.Avalonia/Views/MainWindow.axaml`; `BrowseCommand` and drive scan commands have no `CanExecute` guard in `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, while keyboard bindings remain attached.
- Files: `SizeScanner.Avalonia/Views/MainWindow.axaml`, `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/Services/ScanService.cs`
- Impact: A keyboard or programmatic command can overlap scans. The older operation can publish progress or a result after the newer one, dispose the newer cancellation source, and read inaccessible paths from the wrong `DriveScanner`.
- Fix approach: Make `ScanTargetAsync` single-flight, cancel and await any prior operation, add `CanExecute = !IsScanning` to every scan command, and keep operation-scoped scanner/CTS/result objects rather than mutable service properties.

**Mutable scanner service state:**
- Issue: `ScanService.RunAsync` assigns `Scanner = new DriveScanner()` and the `Task.Run` lambda dereferences that property instead of a local variable.
- Files: `SizeScanner.Avalonia/Services/ScanService.cs`, `SizeScanner.Avalonia/Abstractions/IScanService.cs`
- Impact: Concurrent calls can execute against or expose the wrong scanner instance; `LastTarget`, `IsDriveScan`, and `Scanner` do not describe one atomic completed operation.
- Fix approach: Capture `var scanner = new DriveScanner()` before scheduling, return an operation result containing root and metadata, and synchronize or reject concurrent service calls.

**Public threshold API has stale and misleading state:**
- Issue: `_occupied` is set only by `ScanDrive`; reusing one `DriveScanner` for a later directory scan leaves the previous drive value, and `GetDisplayThreshold` never includes actual free space.
- Files: `ScannerCore/DriveScanner.cs`, `ScannerCore.Tests/DriveScannerTests.cs`
- Impact: Direct consumers can receive a drive-based percentage for a directory scan. The production `ScanService` avoids this by constructing a new scanner, but the public API does not enforce that lifecycle.
- Fix approach: Reset `_occupied` for every scan, rename or remove the unused threshold method, and compute chart thresholds exclusively through `SizeScanner.Avalonia/Charting/FilterThreshold.cs`.

**Unfinished MFT engine seam:**
- Issue: `ScanEngineSelector` registers only `DirectoryWalkEngine`; no `MftScanEngine`, `NtfsVolume`, parser, reader, or tree builder exists.
- Files: `ScannerCore/DriveScanner.cs`, `ScannerCore/ScanEngineSelector.cs`, `docs/superpowers/specs/2026-06-17-mft-scan-engine-design.md`
- Impact: Elevation can improve access but does not provide the advertised design's whole-volume acceleration; large NTFS scans remain syscall-per-directory walks.
- Fix approach: Implement the parser and tree builder as pure tested components before raw-volume I/O, or explicitly defer/remove the draft fast-path promise.

**Documentation and pipeline drift:**
- Issue: `AGENTS.md` describes `.gitlab-ci.yml` and x86/x64 AnyCPU support, but no `.gitlab-ci.yml` is tracked and every project/release is fixed to `win-x64`.
- Files: `AGENTS.md`, `ScannerCore/ScannerCore.csproj`, `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`, `.github/workflows/release.yml`
- Impact: Contributors plan against nonexistent CI and unsupported architecture claims.
- Fix approach: Restore and test the GitLab pipeline or remove the claim; document x64-only support or add separate RID builds and interop validation.

## Known Bugs

**Unhandled scan failure leaves the UI in scanning state:**
- Symptoms: Exceptions other than cancellation escape `ScanTargetAsync`; `IsScanning`, status text, and `_scanCts` are not restored.
- Files: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/Services/ScanService.cs`
- Trigger: A drive disappears, `DriveInfo` throws, native enumeration throws, or an engine exhausts all fallbacks.
- Workaround: Restart the application; controls remain disabled because `IsScanning` stays true.

**Overlapping scans can corrupt operation state:**
- Symptoms: Progress, chart root, inaccessible paths, cancellation, and `Scanner` metadata can come from different scans.
- Files: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/Services/ScanService.cs`, `SizeScanner.Avalonia/Views/MainWindow.axaml`
- Trigger: Invoke Ctrl+O or another command path while a scan is active; commands themselves do not reject execution.
- Workaround: Do not invoke scan key bindings until the active scan finishes.

**Native query failures silently produce partial trees:**
- Symptoms: Any `NtQueryDirectoryFile` status other than success or `STATUS_NO_MORE_FILES` only writes to `Debug`; the already collected list is returned as a successful directory.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/DirectoryWalkEngine.cs`
- Trigger: Mid-enumeration I/O errors, filesystem-driver errors, device removal, or unsupported native status.
- Workaround: None in the release UI; affected bytes are omitted without adding the directory to `DriveScanner.Inaccessible`.

**Hard links can inflate totals:**
- Symptoms: Each directory entry contributes its allocation size, with no file-ID deduplication; drive progress can exceed 100% and `[Inaccessible]` clamps to zero.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/DirectoryWalkEngine.cs`, `ScannerCore/DriveScanner.cs`
- Trigger: Scan a volume containing multiple hard links to the same file record.
- Workaround: Interpret totals as path-entry totals rather than unique on-disk allocation.

**Synthetic names collide with legal filesystem names:**
- Symptoms: Real entries named `[Free space]`, `[Inaccessible]`, `[Filtered]`, or `[Other]` receive synthetic colors/rules, may be excluded from used totals, cannot be scoped, and lose their context menu.
- Files: `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`, `SizeScanner.Avalonia/Charting/FilterThreshold.cs`, `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`
- Trigger: Scan any directory containing an entry with one of the reserved display names.
- Workaround: Rename the real filesystem entry.

**Context menu can retarget a destructive action:**
- Symptoms: `_lastRightClickPosition` is never cleared; context-menu opening re-hit-tests that stale coordinate and can override a keyboard-selected or previously prepared target.
- Files: `SizeScanner.Avalonia/Views/ChartView.axaml.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`
- Trigger: Open a context menu through keyboard/programmatic input after a prior right-click or after layout changes.
- Workaround: Right-click the intended segment immediately before choosing delete.

**Deep or long paths can be reported as inaccessible:**
- Symptoms: Native `CreateFile` receives ordinary paths without an extended-length prefix, the application manifest has no `longPathAware` declaration, and both scanning and chart processing recurse.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/DirectoryWalkEngine.cs`, `SizeScanner.Avalonia/app.manifest`, `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`
- Trigger: Deep trees exceed effective Win32 path limits or recursion depth.
- Workaround: Scan a shallower subtree or enable host/OS long-path policy; recursion has no workaround for pathological depth.

## Security Considerations

**Unsafe native buffer parsing:**
- Risk: Fixed offsets, `NextEntryOffset`, and `FileNameLength` are trusted without checking `IO_STATUS_BLOCK.Information`; malformed output from a filesystem driver can cause out-of-bounds reads and process crashes.
- Files: `ScannerCore/DirectoryScanner.cs`
- Current mitigation: The source is a kernel API and the rented buffer is 1 MiB, but neither condition establishes per-record bounds.
- Recommendations: Pass the returned byte count into `ParseBuffer`, validate alignment/ranges/UTF-16 lengths, stop on malformed records, and fuzz a span-based pure parser.

**Elevated destructive operations:**
- Risk: Relaunching as administrator gives delete operations full elevated rights, while the delete path is reconstructed from mutable filesystem names and revalidated only with `File.Exists`/`Directory.Exists`.
- Files: `SizeScanner.Avalonia/Services/WindowsElevationService.cs`, `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`
- Current mitigation: A confirmation dialog precedes recycle-bin and permanent deletion, and reparse entries are generally omitted during scanning.
- Recommendations: Show persistent elevated-state UI, canonicalize and verify the target remains under the scanned root, reject synthetic nodes inside the command itself, and revalidate file identity immediately before deletion.

**Unsigned release artifacts:**
- Risk: Users cannot verify publisher identity, UAC displays an unknown publisher, and a replaced release binary is harder to distinguish from an official build.
- Files: `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`
- Current mitigation: GitHub hosts the generated zip and release notes.
- Recommendations: Authenticode-sign the executable, publish SHA-256 checksums/SBOM, and verify signing before release upload.

**Mutable CI dependencies and automatic dependency merging:**
- Risk: GitHub Actions use moving major-version tags, and patch/minor Dependabot updates are automatically queued for merge.
- Files: `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, `.github/workflows/codeql.yml`, `.github/workflows/dependabot-auto-merge.yml`
- Current mitigation: GitHub permissions are mostly scoped per workflow, and branch protection can gate auto-merge outside repository code.
- Recommendations: Pin third-party actions to immutable commit SHAs, require all Windows build/test/publish checks before auto-merge, and limit release workflow dependencies.

## Performance Bottlenecks

**Recursive nested parallelism:**
- Problem: Every parallel child recursively calls `WalkDirectory(..., parallelChildren: true)`, creating nested `Parallel.ForEach` regions throughout the tree.
- Files: `ScannerCore/DirectoryWalkEngine.cs`, `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`
- Cause: The top-level parallel flag is propagated into descendants instead of switching each worker to a sequential subtree walk.
- Improvement path: Use one bounded producer/consumer work queue or parallelize only root subdirectories; test the global maximum concurrency with an injected scanner.

**One-megabyte buffer per concurrent enumeration:**
- Problem: Each active `DirectoryScanner.Scan` rents and pins a 1 MiB array; recursive parallelism can create many simultaneous rentals and leave large arrays retained in the shared pool.
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore/DirectoryWalkEngine.cs`
- Cause: Per-call buffers combine with globally unbounded nested fan-out.
- Improvement path: Bound workers globally, reuse one buffer per worker, and benchmark smaller buffers against syscall count.

**Render cap does not cap preprocessing:**
- Problem: `MaxSegments` and `MaxSegmentsPerSector` limit emitted segments only after `CountRings`, `ComputeDisplayedSizes`, candidate collection, and candidate sorting traverse the tree.
- Files: `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`, `SizeScanner.Avalonia.Tests/SunburstChartBuilderCapTests.cs`
- Cause: `EmitRing` creates a candidate tuple for every visible child and sorts the full list even when at most 100 segments can be emitted for a sector.
- Improvement path: Select only the largest budgeted children with a bounded heap/partial selection, aggregate the remainder during one pass, and avoid `_displayedSize` entries for nodes that cannot render.

**Full in-memory filesystem tree:**
- Problem: Every entry retains an `FsItem`, name string, parent pointer, and directory `List<FsItem>` before the chart adds dictionaries, segments, geometry, brushes, and indexes.
- Files: `ScannerCore/FsItem.cs`, `ScannerCore/DirectoryWalkEngine.cs`, `SizeScanner.Avalonia/Views/SunburstChartControl.cs`
- Cause: Scanning and visualization have no streaming, compaction, or spill-to-disk layer.
- Improvement path: Measure bytes per node, finalize children into compact arrays, intern only proven-repetitive metadata, and build summarized chart data without duplicating the complete tree.

**Real performance gate is opt-in:**
- Problem: The speed test is skipped in normal local and CI runs.
- Files: `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`, `.github/workflows/dotnet-desktop.yml`
- Cause: It scans `C:\` and depends on machine storage.
- Improvement path: Add deterministic injected-I/O concurrency benchmarks and schedule the real-volume test on a dedicated Windows performance runner.

## Fragile Areas

**Native directory record layout:**
- Files: `ScannerCore/DirectoryScanner.cs`, `ScannerCore.Tests/DirectoryScannerParsingTests.cs`
- Why fragile: Correctness depends on hard-coded offsets, structure packing, NTSTATUS behavior, synchronous handle semantics, and x64 marshalling.
- Safe modification: Keep parsing pure and bounds-checked; validate x86/x64 layouts if multiple architectures are claimed; preserve a live Windows integration test.
- Test coverage: Existing tests enumerate normal temp directories but do not exercise malformed records, odd lengths, partial native failures, offline placeholders, or reparse points.

**Tree mutation after delete:**
- Files: `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`
- Why fragile: A hidden-free-space chart root shares child objects with the real scan root, while deletion removes from both lists and subtracts size through parent links.
- Safe modification: Centralize mutation in a tree service, enforce parent/list invariants, and rebuild derived roots from the authoritative tree after changes.
- Test coverage: Top-level deletion is covered, but scoped deletion, repeated deletion, concurrent filesystem changes, and mutation failure are not.

**Synthetic entry position and identity:**
- Files: `ScannerCore/DriveScanMetadata.cs`, `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`, `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`
- Why fragile: Some code relies on fixed indices while other code relies on names, and ordinary `FsItem` instances carry no explicit synthetic kind.
- Safe modification: Add a typed node kind/flags field and use it consistently; reserve indices only at the drive-root boundary.
- Test coverage: Synthetic happy paths are covered, but collisions with real names and reordered/missing entries are not.

**Error reporting and diagnostics:**
- Files: `ScannerCore/ScanEngineSelector.cs`, `ScannerCore/DirectoryScanner.cs`, `SizeScanner.Avalonia/Program.cs`, `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`
- Why fragile: Engine failures and native errors go only to debug/trace output, while settings load errors are swallowed and scan exceptions have no user-facing path.
- Safe modification: Introduce structured logging and typed scan warnings/errors, then map recoverable failures to the UI without discarding diagnostics.
- Test coverage: No tests assert release-visible diagnostics or recovery from scan/settings-save failures.

## Scaling Limits

**Filesystem nodes:**
- Current capacity: One managed tree node per filesystem entry in `ScannerCore/FsItem.cs`; no explicit node limit exists.
- Limit: Memory grows O(entries), and recursive traversal grows O(depth) in `ScannerCore/DirectoryWalkEngine.cs`.
- Scaling path: Add memory benchmarks for million-node synthetic trees, iterative traversal, compact finalized child storage, and optional summary-mode scans.

**Chart segments and candidates:**
- Current capacity: Global render cap 100,000 and per-root-sector budget 100 in `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`.
- Limit: Candidate lists and sorting remain unbounded by those caps; geometry caching remains O(emitted segments) in `SizeScanner.Avalonia/Views/SunburstChartControl.cs`.
- Scaling path: Apply budgets before sorting/allocation and expose a visible truncation indicator with measured memory/time budgets.

**Inaccessible-path reporting:**
- Current capacity: Every failed path is stored as a full string in `DirectoryWalkEngine.WalkContext`.
- Limit: A volume with widespread access failures consumes memory proportional to the number and length of paths and then duplicates them into `MainWindowViewModel.InaccessiblePaths`.
- Scaling path: Cap displayed samples, retain counts and aggregate size/reasons, and support exporting full diagnostics on demand.

## Dependencies at Risk

**`NtQueryDirectoryFile` native contract:**
- Risk: The scanner depends directly on `ntdll.dll` structures/status values without generated interop or defensive versioning.
- Impact: ABI mistakes or unusual filesystem drivers can silently truncate scans or crash unsafe parsing.
- Migration plan: Wrap the call behind a narrow native adapter, add a bounds-checked parser, and evaluate documented Win32 enumeration or `NtQueryDirectoryFileEx` where performance permits.

**`Microsoft.VisualBasic.FileIO` recycle-bin API:**
- Risk: Recycle-bin behavior is delegated to legacy shell-backed helpers from a worker thread and has minimal integration coverage.
- Impact: Apartment, shell, long-path, reparse, or policy-specific failures reach users only as exception messages.
- Migration plan: Add Windows integration tests for recycle-bin files/directories and evaluate a dedicated `IFileOperation` interop implementation in `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`.

**Moving GitHub Action tags:**
- Risk: `actions/*@v4`, `github/codeql-action/*@v3`, and `softprops/action-gh-release@v2` are not immutable.
- Impact: Workflow behavior can change without a repository diff.
- Migration plan: Pin reviewed SHAs and let Dependabot propose explicit SHA updates in `.github/dependabot.yml`.

## Missing Critical Features

**User-visible scan error recovery:**
- Problem: Fatal scan errors have no dialog or recoverable error state.
- Blocks: Reliable handling of removable drives, native failures, and future engine fallback exhaustion.
- Files: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`, `SizeScanner.Avalonia/Abstractions/IDialogService.cs`

**Admin NTFS fast path:**
- Problem: The draft MFT engine is absent.
- Blocks: The intended seconds-scale whole-volume scan path and accurate file-reference-based hard-link handling.
- Files: `docs/superpowers/specs/2026-06-17-mft-scan-engine-design.md`, `ScannerCore/ScanEngineSelector.cs`

**Signed, verified Windows distribution:**
- Problem: Release output is an unsigned zip with no checksums or SBOM.
- Blocks: Publisher trust and strong artifact provenance for an application that can elevate and permanently delete files.
- Files: `.github/workflows/release.yml`, `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`

## Test Coverage Gaps

**Filesystem semantics:**
- What's not tested: Hard links, junctions/symlinks, OneDrive offline placeholders, inaccessible directories, long paths, disappearing devices, partial NTSTATUS failures, and deep trees.
- Files: `ScannerCore.Tests/DirectoryScannerParsingTests.cs`, `ScannerCore.Tests/DirectoryWalkEngineTests.cs`, `ScannerCore/DirectoryScanner.cs`
- Risk: Silent size errors, unintended traversal, incomplete trees, or crashes can pass all tests.
- Priority: High

**Parallelism behavior:**
- What's not tested: A forced parallel policy, global concurrency bounds, nested-tree scheduling, cancellation during active enumeration, and buffer pressure.
- Files: `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`, `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`, `ScannerCore/DirectoryWalkEngine.cs`
- Risk: Performance regressions, thread-pool starvation, and delayed cancellation remain undetected.
- Priority: High

**UI operation races and failures:**
- What's not tested: Overlapping scans, scan exceptions, stale progress, command `CanExecute` during scans, settings-save failures, window close during scanning, and stale context-menu coordinates.
- Files: `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`, `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`, `SizeScanner.Avalonia/Views/ChartView.axaml.cs`
- Risk: The UI can become stuck or act on the wrong scan/node.
- Priority: High

**Destructive filesystem behavior:**
- What's not tested: Recycle-bin success, recursive directory deletion, reparse targets, target replacement races, long paths, and elevated/protected locations.
- Files: `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`, `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`
- Risk: Delete behavior can fail or affect an unexpected target without regression coverage.
- Priority: High

**Synthetic-name collisions:**
- What's not tested: Real files/directories using chart metadata names.
- Files: `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`, `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`, `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`
- Risk: Legal user data is rendered and acted on incorrectly.
- Priority: Medium

**Release guarantees:**
- What's not tested: Native AOT publish on pull requests, analyzer warnings as errors, minimum coverage, signed artifact verification, and GitLab CI parity.
- Files: `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, `Directory.Build.props`, `AGENTS.md`
- Risk: Trimming/AOT, quality, or packaging regressions are discovered only after a tag or by users.
- Priority: Medium

---

*Concerns audit: 2026-07-16*
