# Shallow Parallel Fan-Out Design

**Date:** 2026-08-06  
**Status:** Reviewed — revision 2  
**Branch:** `feat/bounded-streaming-snapshot`  
**Related:** bounded streaming snapshot walker (`BoundedDirectoryWalker`)  
**Goal priority:** Close most of the NVMe long-pole gap with a bounded, simple change; accept the residual single-directory tail (Option B).

---

## Problem

`BoundedDirectoryWalker` fans out only at the scan root, with `ScanTreeBudget.MaxDegreeOfParallelism` defaulting to `4`. Once the small top-level directories finish, the remainder of a `C:\` scan (usually `Windows` or `Users`) is walked by a single thread while the rest of the pool idles.

The pre-branch `DirectoryWalkEngine` used recursive `Parallel.ForEach` at `Environment.ProcessorCount`, which kept threads busy deeper in the tree. The current tests only prove parallel ≡ sequential totals and a bounded open count; nothing detects long-pole under-utilization.

Full work-stealing (workers pushing every newly discovered subdirectory onto a shared queue with parent joins) would close the gap furthest but is deferred. This design takes the cheaper middle ground.

---

## Goals

1. On SSD/NVMe volumes, keep the pool busy one level below the root, so `Windows\*` and `Users\*` subtrees progress concurrently.
2. Bound concurrency, memory, and handles explicitly — one shared concurrency domain, no nested pools.
3. Preserve exact totals, inaccessible semantics, cancellation, bounded retention, and HDD sequential policy.
4. Prove the long-pole fix with a deterministic test, and pick defaults from a measurement on a real volume.

## Non-goals

- Full recursive work-stealing / arbitrary-depth parallel queues.
- Matching pre-branch wall-clock on every volume shape.
- Changing chart/UI contracts, `FsItem` retention rules, or native enumeration.
- Parallelism on seek-penalty volumes (`VolumeParallelismPolicy` still forces sequential).

---

## Approach summary

**Depth-limited fan-out under one shared slot budget, async through the fan-out levels only.**

- A directory fans its children out iff `depth < ParallelFanOutLevels`. Default `2` — the root and its immediate children.
- Everything at or below that depth walks its whole subtree synchronously on a single slot, exactly as today.
- One `SemaphoreSlim(MaxDegreeOfParallelism)` for the entire scan. A slot is held only around a native read or around a sequential subtree walk, **never across a wait for children**.
- Children are scheduled in a bounded window as batches are read, so subdirectory names are never fully materialized.

```text
ParallelFanOutLevels = 2

C:\                     depth 0  < 2  → fan out children
  Windows\              depth 1  < 2  → fan out children
    System32\           depth 2 !< 2  → whole subtree on one slot
    WinSxS\             depth 2 !< 2  → whole subtree on one slot
  Users\                depth 1  < 2  → fan out children
    Alice\              depth 2 !< 2  → whole subtree on one slot
```

`ParallelFanOutLevels = 1` reproduces today's root-only behavior; `0` is fully sequential. The volume policy simply forces `0`.

---

## Architecture

### Current behavior (baseline)

1. `DirectoryWalkEngine` derives `parallelizeTopLevel` from `VolumeParallelismPolicy.ShouldParallelize(target)`.
2. `BoundedDirectoryWalker.Scan` passes `parallelizeChildren: true` only into the root `WalkDirectory` call.
3. `WalkChildrenInParallel` drains the root cursor into a full `List<string>` of subdirectory names, then `RunChildWorkersAsync` creates a private channel pair plus `degree` worker tasks and blocks on `.GetAwaiter().GetResult()`.
4. Each worker rents its own 1 MiB `ArrayPool<byte>` buffer for its lifetime and walks its assigned subtree sequentially.

### Target structure

Three methods replace the `parallelizeChildren` boolean:

| Method | Sync/async | Role |
|--------|-----------|------|
| `Scan` | sync | Creates the shared context, runs the root walk, single blocking boundary |
| `WalkAsync` | async | Dispatches: fan-out if `depth < ParallelFanOutLevels`, else one slot + sequential subtree |
| `WalkSequentialUnderSlot` | sync | Today's `WalkDirectory`/`WalkChildrenSequentially`, unchanged semantics |
| `WalkFanOutAsync` | async | Batch-reads under a slot, schedules children in a bounded window, folds results |

Async state machines therefore exist only at the top `ParallelFanOutLevels` levels (default two). The deep hot path stays synchronous and allocation-free.

### Slot discipline (this is the deadlock proof)

A slot is acquired:

- around a single `cursor.ReadNext(buffer, sink)` call in `WalkFanOutAsync`, released immediately after; and
- for the entire duration of a `WalkSequentialUnderSlot` subtree.

A slot is **never** held while awaiting a child. A slot holder therefore never waits on another slot holder, so no wait cycle can form and the pool cannot deadlock regardless of nesting depth. This replaces the earlier "pick one of three options" hand-wave.

Consequence: peak concurrent `ReadNext` calls ≤ `MaxDegreeOfParallelism`. That is the invariant tests assert — not open-handle count.

### Bounded child scheduling (memory)

The current parallel path buffers every subdirectory name before fanning out, abandoning the batch-by-batch streaming that bounds the sequential path. Fan-out at a second level would multiply that. Instead:

- Read one batch under a slot; release the slot.
- For each subdirectory name in that batch, schedule a child walk. If the in-flight window is full, first drain one completed child (`Task.WhenAny`), fold it into the collector, and add its size.
- Window size is `2 × MaxDegreeOfParallelism`, internal — no new budget knob.
- After the last batch, drain the remaining window.

Names alive at any moment are therefore one native batch plus the in-flight window, not the whole directory.

### Buffers

Rent from `ArrayPool<byte>.Shared` **after** acquiring a slot and return **before** releasing it. Outstanding buffers are then ≤ `MaxDegreeOfParallelism`, i.e. ≤ DOP MiB at the current `DirectoryScanner.BufferSize` of 1 MiB. A fan-out parent rents and returns per batch; a sequential subtree rents once for its whole walk.

### Handles

A fan-out parent keeps its cursor open across awaits, so open cursors are bounded by
`1 + (levels − 1) × window + degree` — roughly 50 handles at DOP 16, levels 2. This is stated rather than asserted; the concurrency assertion targets `ReadNext`.

### Collector ownership

`BoundedChildCollector` is not thread-safe. Invariant: **each directory's collector is touched by exactly one flow** — its own. Files enter via the parent's `BatchSink` during its own reads; children enter via the parent's drain loop. Both run on the parent's async flow, serialized by `await`. Workers only ever touch collectors of directories they own.

Retention is unaffected by scheduling: `BoundedChildCollector` ranks by size then ordinal name, so completion order cannot change the retained set, hidden aggregate, or display order.

### Failure and cancellation

- `WalkContext` owns a `CancellationTokenSource` linked to the caller's token. The walk uses the linked token.
- The first non-cancellation failure is captured (`Interlocked.CompareExchange`) and cancels the linked source, so sibling subtrees stop instead of walking to completion behind a doomed scan.
- A parent that catches a child failure observes its remaining in-flight tasks (swallowing derived `OperationCanceledException`) before rethrowing, so no task goes unobserved.
- `Scan` rethrows the captured original failure via `ExceptionDispatchInfo` when the caller's token was not cancelled. User cancellation still surfaces as `OperationCanceledException`, and no partial `ScanResult` is published in either case.

### Volume policy

`DirectoryWalkEngine` keeps deriving one boolean from `VolumeParallelismPolicy`. When false, the walker uses `ParallelFanOutLevels = 0` and `degree = 1`, which is byte-for-byte today's sequential path.

---

## API / budget knobs

`ScanTreeBudget` gains one parameter, appended last to keep existing positional/named calls valid:

| Property | Default | Meaning |
|----------|---------|---------|
| `MaxDegreeOfParallelism` | `Math.Min(Environment.ProcessorCount, 16)` (confirm by measurement) | Shared slots for concurrent native reads and sequential subtrees |
| `ParallelFanOutLevels` | `2` | Number of levels that fan out: `0` sequential, `1` root only (today), `2` root + one level |

Validation: `maxDegreeOfParallelism >= 1`, `parallelFanOutLevels >= 0`. No negative sentinel.

Because the DOP default now depends on the machine, **every concurrency test must pass an explicit budget**; a two-core CI agent would otherwise make parallel assertions vacuous.

No new engine parameters; `IScanEngine.Scan` and `ScanResult` are unchanged.

---

## Correctness constraints (must hold)

- Exact reachable byte totals.
- `Items == null` only for inaccessible directories; inaccessible sampling stays capped.
- Aggregate and `HasUnretainedChildren` semantics unchanged, including the `allowance <= 1` case that attaches no children.
- Unexpected native enumeration failure aborts the scan; cancellation throws; neither publishes a partial result.
- Peak concurrent `ReadNext` ≤ `MaxDegreeOfParallelism`.
- Outstanding pooled buffers ≤ `MaxDegreeOfParallelism`.
- Retained tree is identical for any DOP / fan-out level combination.
- Native AOT and trimming compatible; no new NuGet dependency.

---

## Testing

### Test infrastructure (prerequisite)

`SyntheticDirectoryEntrySource` only serves a flat directory — it returns a cursor for the exact root path and `null` for everything else, so every child reads as inaccessible. It cannot express a tree, and nothing else in the project can either. A hierarchical fake source is therefore a prerequisite, not a detail:

- `SyntheticTreeSource` — builds from a nested node description, resolves any path, emits entries in 256-item batches.
- `ConcurrencyProbe` — tracks current/peak concurrent reads and can block designated paths until a target concurrency is observed or a timeout elapses.

### Required tests

1. **Long-pole rendezvous (the point of the change).** Tree: root → one child → two grandchildren, `ParallelFanOutLevels = 2`, `MaxDegreeOfParallelism = 2`. Both grandchild reads gate on observing concurrency 2. Passes under two-level fan-out; times out under root-only, which is exactly the regression being fixed. Deterministic — no wall-clock comparison.
2. **Negative control.** Same tree at `ParallelFanOutLevels = 1` never reaches concurrency 2 (short timeout).
3. **Full equivalence.** Recursive comparison of the entire retained tree (names, sizes, `IsDir`, `HasUnretainedChildren`, `Items == null`) plus `Total`, `InaccessibleCount`, across sequential vs levels 1 vs levels 2, and across two different DOP values.
4. **Concurrency cap.** Peak concurrent `ReadNext` ≤ DOP under two-level fan-out on a deep, wide synthetic tree.
5. **No deadlock under scarcity.** Nested fan-out with `MaxDegreeOfParallelism = 1` and `ParallelFanOutLevels = 2` completes (single slot forces every parent to release before children can run).
6. **Failure propagation.** A cursor that returns `DirectoryBatchResult.Failed` deep in one subtree surfaces `IOException` from `Scan` (not `OperationCanceledException`, not `AggregateException`) and does not hang.
7. **Cancellation.** Cancelled token throws `OperationCanceledException` under two-level fan-out.
8. **Policy off.** `new DirectoryWalkEngine(_ => false)` stays sequential and correct.
9. **Budget validation.** `parallelFanOutLevels: -1` rejected; `0` disables fan-out; `1` is root-only.
10. **Memory.** Existing `BoundedScanMemoryTests` still pass, plus a wide-directory fan-out case asserting retained nodes stay within budget.

### Measurement

Extend the existing `DirectoryWalkEngineParallelSpeedTests`, which already handles the hard parts: `SIZESCANNER_RUN_PERF_TESTS=1` gating, an SSD-policy skip guard, GC reset, alternating order, and median-of-rounds. It only hardcodes `ScanTreeBudget.Default` inside `MeasureScan`; parameterize that by budget and iterate configs. No new `ScannerConsole` flags.

| Config | `ParallelFanOutLevels` | `MaxDegreeOfParallelism` | Purpose |
|--------|------------------------|--------------------------|---------|
| A | `0` | 1 | Sequential baseline |
| B | `1` | 4 | Current branch behavior |
| C | `1` | `ProcessorCount` | DOP-only delta |
| D | `2` | `ProcessorCount` | Target design |
| E | `3` | `ProcessorCount` | Diminishing-return check |

Decision rule: ship D's settings if D beats B and C materially and E adds little. If E is dramatically better, that is evidence for real work-stealing (separate design) rather than raising the default further. If D ≈ C, keep levels at `1` and ship the DOP bump alone.

---

## Files touched

| File | Change |
|------|--------|
| `ScannerCore/ScanTreeBudget.cs` | Add `ParallelFanOutLevels`; DOP default |
| `ScannerCore/BoundedDirectoryWalker.cs` | Async-through fan-out, slot discipline, bounded window, linked-CTS failure handling |
| `ScannerCore/DirectoryWalkEngine.cs` | Doc comment only |
| `ScannerCore.Tests/SyntheticTreeSource.cs` | New hierarchical fake source + concurrency probe |
| `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs` | Rendezvous, equivalence, cap, deadlock, failure, cancellation |
| `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs` | Budget-parameterized measurement matrix |
| `ScannerCore.Tests/ScanTreeBudgetTests.cs` | Validation |
| `AGENTS.md` | Parallelism bullet |

No Avalonia, chart, `DirectoryScanner`, or `FsItem` changes.

---

## Risks

| Risk | Mitigation |
|------|------------|
| Nested waits deadlock | Slot discipline makes wait cycles impossible; scarcity test at DOP 1 |
| Memory regression from name materialization | Bounded scheduling window; names never fully materialized |
| Buffer pressure at high DOP | Rent inside slot only; ceiling is DOP × 1 MiB |
| Thread-pool starvation from sync-over-async | Exactly one blocking wait, in `Scan`, on the caller's thread |
| Machine-dependent default making CI tests vacuous | Concurrency tests pass explicit budgets |
| Residual long pole inside one huge deep directory | Accepted under Option B; work-stealing is the follow-up |

---

## Decisions

| Question | Decision |
|----------|----------|
| Success bar | Option B — close most of the gap, not full parity |
| Knob shape | `ParallelFanOutLevels` count, default `2`; no negative sentinel |
| Concurrency model | Async through fan-out levels; single blocking boundary in `Scan` |
| Deadlock avoidance | Slot never held across a child wait |
| Nested pools | Forbidden — one `SemaphoreSlim` per scan |
| Work-stealing | Out of scope |
| DOP default | `Math.Min(ProcessorCount, 16)`, confirmed by the measurement matrix |

---

## Measured results

**Date:** 2026-08-06
**Machine:** CPU `AMD Ryzen 7 5800X 8-Core Processor` (8 cores / 16 logical processors, via `Get-CimInstance Win32_Processor`); OS Windows 11 Pro 10.0.26200.
**C: physical drive:** `WD_BLACK SN850X 2000GB`, NVMe, SSD media type — identity confirmed by resolving the drive letter to its physical disk (`Get-Partition -DriveLetter C | Get-Disk`), not just enumerating all disks, since this machine has two SSDs and `C:` had to be matched to the correct one.
**Volume policy:** `VolumeParallelismPolicy.ShouldParallelize("C:\")` returns `true` (no seek penalty reported), so the perf-test SSD guard passed and the matrix ran for real rather than skipping.

### Command

```powershell
$env:SIZESCANNER_RUN_PERF_TESTS = "1"
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~Fan_out_configuration_matrix_report" --logger "console;verbosity=detailed"
```

### Raw output (5 configs × 2 rounds each, 10 full `C:\` scans)

```
 A sequential         levels=0 dop=1  median=38,62s total=395 086 822 488
 B root-only dop4     levels=1 dop=4  median=12,97s total=395 088 669 840
 C root-only dopN     levels=1 dop=16 median=12,28s total=395 089 255 632
 D two-level dopN     levels=2 dop=16 median=12,51s total=395 087 113 480
 E three-level dopN   levels=3 dop=16 median=8,60s total=395 089 259 240

Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 2,6637 Minutes
```

### Totals check — investigated, not a walker bug

The five totals are **not byte-identical** (they span roughly 395,086,822,488 – 395,089,259,240, a spread of ~2.44 MB out of ~368 GiB, i.e. ~6×10⁻⁶ relative). Per the decision rule this is treated as a potential correctness signal and was investigated before drawing any tuning conclusion, rather than assumed benign.

Investigation: a throwaway, uncommitted test ran the **same** config (`levels=0, dop=1`, sequential) four times back-to-back against the same live `C:\` volume:

```
run 0: total=395 064 355 568 elapsed=20,79s
run 1: total=395 064 601 328 elapsed=20,34s
run 2: total=395 064 961 776 elapsed=27,21s
run 3: total=395 064 953 584 elapsed=24,48s
```

Even with the config held fixed, the total drifts by ~606 KB over ~93 seconds of wall-clock time, and drifts in the same direction (mostly increasing) as the 5-config matrix. `C:\` is this machine's live, in-use system+user volume (browser cache/history, temp files, Windows/app logs, prefetch); it is not a static fixture, so small monotonic growth between temporally separated scans is expected background churn, not a fan-out/DOP concurrency defect. This is consistent with — and does not contradict — the deterministic, byte-for-byte equivalence already proven on static synthetic trees across sequential/levels-1/levels-2 and multiple DOP values (`DirectoryWalkEngineParallelTests`, full-tree equivalence test). The throwaway investigation test was not committed; only the brief's exact matrix code and results are recorded here.

**Conclusion:** the five totals differ by an amount and pattern fully explained by live-volume drift during a ~2.7-minute measurement window, not by the fan-out change. Not BLOCKED — proceeding to the tuning decision.

### Decision-rule reasoning

| Comparison | Values | Verdict |
|---|---|---|
| D vs B | 12.51s vs 12.97s | D marginally faster (~3.5%) |
| D vs C | 12.51s vs 12.28s | D marginally *slower* (~1.9%) — within the noise band demonstrated above (control runs of similar duration varied by several %) |
| E vs D | 8.60s vs 12.51s | E materially faster (~31%) |

- **D materially faster than B and C, E within noise of D?** No — D does not beat C at all (it's marginally slower), so this branch does not apply.
- **D ≈ C?** Yes — the ~0.23s gap between D and C is within the run-to-run noise measured on this same live volume (the control experiment showed ~3% drift in elapsed time and total bytes across identical-config runs). Two-level fan-out buys nothing measurable over just raising `MaxDegreeOfParallelism` at the existing root-only fan-out.
- **E materially faster than D?** Yes, clearly (~31%), but this does not override the D≈C finding — it only means three-level fan-out finds more usable parallelism than two-level does on this tree shape (`C:\Windows\*` and similar subtrees are still large enough at depth 2 to benefit from being split further).

Applying the rule conservatively: since **D ≈ C**, the decision is to **revert the shipped default to `parallelFanOutLevels: 1`** (root-only, i.e. today's pre-branch fan-out shape) while **keeping `maxDegreeOfParallelism` at its auto default** (`Math.Min(ProcessorCount, 16)`, i.e. `0` resolved). The measurable win on this machine is the DOP bump from 4 to 16 (B → C, ~5.3% faster), not the extra fan-out level. `parallelFanOutLevels: 2` (and `3`) remain available, tested, and correct knobs — just not the shipped default — and the two-level path stays exercised by the deterministic rendezvous/equivalence/cap/deadlock tests in `DirectoryWalkEngineParallelTests`.

**Follow-up note (from the E result):** three-level fan-out (E) was ~31% faster than two-level (D) on this machine's `C:\`, well outside noise. That is evidence that shallow fan-out is leaving real parallelism on the table deeper in the tree (e.g. inside `Windows\` or large user profile subtrees), and is a concrete data point in favor of the deferred full work-stealing design mentioned in Non-goals/Risks, rather than simply raising `ParallelFanOutLevels` again by fixed increments.

### Resulting change (initial, superseded by the confirmation run below)

`ScannerCore/ScanTreeBudget.cs`: default `parallelFanOutLevels` changed from `2` to `1`. `maxDegreeOfParallelism` default (`0` → auto `Math.Min(ProcessorCount, 16)`) is unchanged — it was already confirmed correct by this measurement (C, D, E all use it and all beat A and B).

**Caveat identified after the fact:** the grouped `A→B→C→D→E` order above always measures each config's two samples back-to-back before moving on, so the last config (E) is always measured against the warmest filesystem cache and the first config (A) always against the coldest. That is a real order confound, not just a hypothetical one — see the balanced confirmation run below, where the same config (A) measured cold vs. warm differs by ~27%, dwarfing the ~3–4% run-to-run noise seen on the parallel configs. The decision below was re-derived from a balanced re-run rather than relying on the grouped numbers alone.

### Balanced confirmation run

**Why:** the grouped order's cache-warmth confound (above) could not be ruled out as the reason `D` and `C` came out close and `E` came out ahead — an artifact of run position rather than a real property of three-level fan-out. `Fan_out_configuration_matrix_report` was revised to collect its two samples per config across **two global rounds** instead of two back-to-back samples per config: round 1 walks the configs `A→B→C→D→E`, round 2 walks them in reverse, `E→D→C→B→A`. Every config now gets exactly one early-round and one late-round sample (except the middle config, `C`, which is position 3 of 5 in both directions and so is consistently mid-run in both rounds — an accepted limitation of a simple two-round reversal, not a hidden bias toward any other single config). Same 5 exact configs, same 2 samples each, same `GC.Collect`/`WaitForPendingFinalizers`/`GC.Collect` reset inside `MeasureScan`, same `Median` helper, same SSD/perf gates, no new dependency.

Code (`ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`):

```csharp
    [Fact]
    [Trait("Category", "Performance")]
    public void Fan_out_configuration_matrix_report()
    {
        Assert.SkipUnless(RunPerfTests,
            "Set SIZESCANNER_RUN_PERF_TESTS=1 to run the C: fan-out configuration matrix.");
        Assert.SkipUnless(Directory.Exists(MeasurementRoot), $"{MeasurementRoot} is not available.");
        Assert.SkipUnless(VolumeParallelismPolicy.ShouldParallelize(MeasurementRoot),
            $"{MeasurementRoot} is not SSD-class.");

        var processors = Math.Min(Environment.ProcessorCount, 16);
        (string Name, int Levels, int Degree)[] configs =
        [
            ("A sequential",      0, 1),
            ("B root-only dop4",  1, 4),
            ("C root-only dopN",  1, processors),
            ("D two-level dopN",  2, processors),
            ("E three-level dopN",3, processors)
        ];

        var samples = new List<TimeSpan>[configs.Length];
        var totals = new long[configs.Length];
        for (var i = 0; i < configs.Length; i++)
            samples[i] = new List<TimeSpan>(capacity: 2);

        for (var round = 0; round < 2; round++)
        {
            var forward = round % 2 == 0;
            output.WriteLine($"Round {round + 1} order: {(forward ? "A->E" : "E->A")}");

            for (var step = 0; step < configs.Length; step++)
            {
                var index = forward ? step : configs.Length - 1 - step;
                var config = configs[index];
                var engine = new DirectoryWalkEngine(_ => config.Levels > 0);
                var budget = new ScanTreeBudget(
                    maxDegreeOfParallelism: config.Degree,
                    parallelFanOutLevels: config.Levels);

                var elapsed = MeasureScan(engine, budget, out var total);
                samples[index].Add(elapsed);
                totals[index] = total;

                output.WriteLine(
                    $"  {config.Name,-20} levels={config.Levels} dop={config.Degree,-2} " +
                    $"elapsed={elapsed.TotalSeconds:F2}s total={total:N0}");
            }
        }

        for (var i = 0; i < configs.Length; i++)
        {
            output.WriteLine(
                $"{configs[i].Name,-20} levels={configs[i].Levels} dop={configs[i].Degree,-2} " +
                $"median={Median(samples[i]).TotalSeconds:F2}s total={totals[i]:N0}");
        }
    }
```

`MeasureScan` and `Median` are unchanged from the initial run.

#### Command

```powershell
$env:SIZESCANNER_RUN_PERF_TESTS = "1"
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~Fan_out_configuration_matrix_report" --logger "console;verbosity=detailed"
```

#### Raw output (2 global rounds × 5 configs, 10 full `C:\` scans)

```
Round 1 order: A->E
  A sequential         levels=0 dop=1  elapsed=38,08s total=395 078 032 392
  B root-only dop4     levels=1 dop=4  elapsed=13,94s total=395 078 376 320
  C root-only dopN     levels=1 dop=16 elapsed=12,77s total=395 078 593 840
  D two-level dopN     levels=2 dop=16 elapsed=13,04s total=395 079 978 344
  E three-level dopN   levels=3 dop=16 elapsed=9,24s total=395 080 564 104
Round 2 order: E->A
  E three-level dopN   levels=3 dop=16 elapsed=9,49s total=395 080 510 888
  D two-level dopN     levels=2 dop=16 elapsed=13,18s total=395 081 473 480
  C root-only dopN     levels=1 dop=16 elapsed=12,28s total=395 082 550 752
  B root-only dop4     levels=1 dop=4  elapsed=13,82s total=395 083 075 104
  A sequential         levels=0 dop=1  elapsed=27,98s total=395 083 169 312
A sequential         levels=0 dop=1  median=38,08s total=395 083 169 312
B root-only dop4     levels=1 dop=4  median=13,94s total=395 083 075 104
C root-only dopN     levels=1 dop=16 median=12,77s total=395 082 550 752
D two-level dopN     levels=2 dop=16 median=13,18s total=395 081 473 480
E three-level dopN   levels=3 dop=16 median=9,49s total=395 080 510 888

Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 2,7519 Minutes
```

(`Median` of 2 samples with this codebase's `sorted[Length / 2]` implementation returns the **larger/slower** of the two samples, not an average — that convention is unchanged from the original brief and applies identically to both runs below, so it does not bias the *comparison* between them, but "median" here always means "worse of two.")

#### Cache-order effect, directly demonstrated

Config **A** (fully sequential, single slot) was measured cold in round 1 (first, elapsed 38.08s) and warm in round 2 (last, elapsed 27.98s) — a **26.5% swing from position alone**, far larger than any other config's round-to-round spread (B 0.9%, C 3.8%, D 1.1%, E 2.6%). This confirms the human's concern directly: the original grouped order would have systematically flattered whichever config ran last (there, E) and penalized whichever ran first (there, A), rather than measuring only the fan-out/DOP effect. Parallel configs are far less sensitive to this (they're already close to saturating available I/O/CPU, so caching state matters much less), but A's swing alone is enough to invalidate treating the grouped run as free of order bias.

#### Totals: discrepancy re-investigated with this run's own data, not by assumption

The 10 totals from this run span 395,078,032,392 – 395,083,169,312 (~5.1 MB spread, ~1.4×10⁻⁵ relative to ~368 GiB) — the same order of magnitude as the initial run's spread and consistent with (not contradicting) the live-volume-churn explanation already established there. This run's own layout gives an additional, independent confirmation beyond that prior throwaway experiment: listing all 10 totals in the order they were actually measured shows they climb **almost monotonically with wall-clock position, independent of which config produced them** — e.g. round 1's total rises from A (measured 1st) through E (measured 5th), and every round-2 total is greater than or equal to its round-1 counterpart for the *same* config (E's is the sole, negligible exception, −53,216 bytes, ≈1.9×10⁻⁷ relative — consistent with a single background file being briefly smaller, e.g. a rewritten log or cache file, not a scan defect). A total that tracked *configuration* rather than *wall-clock position* would not show this shape. Conclusion unchanged: not BLOCKED, this is live-volume churn.

#### Decision rule re-applied to the confirmed (balanced) medians

| Comparison | Values | Verdict |
|---|---|---|
| D vs B | 13.18s vs 13.94s | D faster (~5.5%) |
| D vs C | 13.18s vs 12.77s | D **slower** (~3.2%) — within the ~3.8% noise band `C` itself showed round-to-round |
| E vs D | 9.49s vs 13.18s | E materially faster (~28.0%) — far outside any parallel config's round-to-round noise (max ~3.8%) |

This reproduces the same shape as the initial grouped run, now with the cache-order confound eliminated: **D ≈ C still holds** (D does not beat C; the gap is inside measured noise) **and E is still materially faster than D**, simultaneously. These are the plan's branch 2 and branch 3 conditions, and they still both fire at once — the balanced run did not resolve the conflict, it reproduced it. Per this task's instruction, that means **do not choose a branch again; return `NEEDS_CONTEXT`** with the numbers instead of re-deriving a pick as Task 6 did.

**Decision status: NEEDS_CONTEXT.** No change was made to `ScannerCore/ScanTreeBudget.cs` or its tests as a result of this confirmation run — current `HEAD` (`parallelFanOutLevels` default `1`, `maxDegreeOfParallelism` default auto `Math.Min(ProcessorCount, 16)`) is left as committed in Task 6, since the balanced evidence does not call for a *different* value, it calls for a human decision between two valid readings of the plan's rule:

- Treat "D ≈ C" as authoritative (as Task 6 did) → keep the default at `parallelFanOutLevels: 1`.
- Treat "E materially faster than D" as the dominant signal → the default should arguably not be `1` at all, but this branch's own text ("leave the default at 2 and add a note that work-stealing is worth a follow-up") does not fit either, since `2` (`D`) does not measurably beat `1` (`C`)'s DOP-only path. Taking `E`'s result at face value most directly argues for evaluating a `3`-level default or, more likely, prioritizing the deferred work-stealing design — not a call this measurement alone should make.

Human input needed on: whether to keep `parallelFanOutLevels: 1` (current `HEAD`, conservative — ships only the confirmed DOP-bump win), promote `parallelFanOutLevels: 3` as the new default given `E`'s reproducible ~28% edge (a materially bigger change than the plan originally scoped, and not validated against non-`C:\` volume shapes), or treat `E`'s result purely as a work-stealing research trigger and leave the shipped default unchanged pending that separate design.
