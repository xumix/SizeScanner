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
