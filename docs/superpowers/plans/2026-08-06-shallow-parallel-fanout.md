# Shallow Parallel Fan-Out Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the bounded walker fan out one level below the scan root under a single shared concurrency budget, so an NVMe `C:\` scan does not end with one thread grinding through `Windows` or `Users`.

**Architecture:** `BoundedDirectoryWalker` becomes async through the top `ParallelFanOutLevels` levels only; below that each subtree walks synchronously on one slot of a scan-wide `SemaphoreSlim`. A slot is held only around a native read or a whole sequential subtree, never across a wait for children, which makes deadlock structurally impossible. Fan-out parents schedule children in a bounded window as batches arrive, so subdirectory names are never fully materialized.

**Tech Stack:** .NET 10, C# 14, Windows `NtQueryDirectoryFile`, xUnit v3, BCL only (no new packages).

**Spec:** `docs/superpowers/specs/2026-08-06-shallow-parallel-fanout-design.md`

## Global Constraints

- Keep filesystem and native logic in `ScannerCore/`; no reference to `SizeScanner.Avalonia`.
- Preserve drive allocation-size and directory logical-size semantics.
- Preserve reparse handling: skip reparse points unless `FILE_ATTRIBUTE_OFFLINE`.
- Preserve `Items == null` exclusively for inaccessible directories.
- Preserve aggregate semantics: `FsItemKind.Aggregate`, empty name, exact hidden size, and `HasUnretainedChildren` on directories with discarded descendants, including the `allowance <= 1` case that attaches no children.
- Cancellation throws and never publishes a partial `ScanResult`; an unexpected `NtQueryDirectoryFile` failure aborts rather than publishing partial totals.
- Peak concurrent `IDirectoryEntryCursor.ReadNext` calls must not exceed `ScanTreeBudget.MaxDegreeOfParallelism`.
- Outstanding `ArrayPool<byte>` rentals must not exceed `ScanTreeBudget.MaxDegreeOfParallelism` (`DirectoryScanner.BufferSize` is 1 MiB).
- The retained tree must be identical for every combination of `MaxDegreeOfParallelism` and `ParallelFanOutLevels`.
- Keep native AOT and trimming compatibility; add no NuGet dependency.
- Every test that asserts on concurrency must pass an explicit `ScanTreeBudget` — the DOP default is machine-dependent.

---

## File Structure

### New files

- `ScannerCore.Tests/SyntheticTreeSource.cs` — hierarchical fake `IDirectoryEntrySource`, concurrency probe, read gate, and injectable read failure. Test-only.

### Modified files

- `ScannerCore/ScanTreeBudget.cs` — `ParallelFanOutLevels`, auto DOP resolution.
- `ScannerCore/BoundedDirectoryWalker.cs` — async-through fan-out, slot discipline, bounded child window, linked-CTS failure handling. Replaces the channel/worker implementation.
- `ScannerCore/DirectoryWalkEngine.cs` — XML doc only.
- `ScannerCore.Tests/ScanTreeBudgetTests.cs` — budget validation.
- `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs` — rendezvous, equivalence, cap, scarcity, failure, cancellation.
- `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs` — budget-parameterized measurement matrix.
- `AGENTS.md` — parallelism bullet.

### Build and test commands

```powershell
dotnet build SizeScanner.slnx -c Debug
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~<TestName>"
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release
```

---

### Task 1: Add `ParallelFanOutLevels` to the scan budget

Adds the knob with today's behavior as the default (`1` = root-only fan-out). Task 5 flips the default to `2` once the walker supports it.

**Files:**
- Modify: `ScannerCore/ScanTreeBudget.cs:12-30`
- Modify: `ScannerCore.Tests/ScanTreeBudgetTests.cs`

**Interfaces:**
- Produces: `ScanTreeBudget.ParallelFanOutLevels` (`int`), constructor parameter `int parallelFanOutLevels = 1` appended last; `maxDegreeOfParallelism: 0` now means "auto" and resolves to `Math.Min(Environment.ProcessorCount, 16)`.
- Preserves: all existing constructor parameter names, order, and defaults; `ScanTreeBudget.Default`.

- [ ] **Step 1: Write the failing tests**

Add to `ScannerCore.Tests/ScanTreeBudgetTests.cs`:

```csharp
    [Fact]
    public void Budget_rejects_negative_fan_out_levels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScanTreeBudget(parallelFanOutLevels: -1));
    }

    [Fact]
    public void Budget_defaults_to_root_only_fan_out()
    {
        Assert.Equal(1, new ScanTreeBudget().ParallelFanOutLevels);
    }

    [Fact]
    public void Zero_degree_resolves_to_bounded_processor_count()
    {
        var budget = new ScanTreeBudget(maxDegreeOfParallelism: 0);

        Assert.Equal(
            Math.Min(Environment.ProcessorCount, 16),
            budget.MaxDegreeOfParallelism);
    }

    [Fact]
    public void Explicit_degree_is_preserved()
    {
        Assert.Equal(3, new ScanTreeBudget(maxDegreeOfParallelism: 3).MaxDegreeOfParallelism);
    }
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~ScanTreeBudgetTests"`
Expected: compile error — `parallelFanOutLevels` does not exist.

- [ ] **Step 3: Implement the budget change**

Replace the constructor and properties in `ScannerCore/ScanTreeBudget.cs`:

```csharp
    /// <param name="maxDegreeOfParallelism">
    /// Shared slots for concurrent native reads and sequential subtree walks.
    /// Zero resolves to <c>Math.Min(Environment.ProcessorCount, 16)</c>.
    /// </param>
    /// <param name="parallelFanOutLevels">
    /// Number of tree levels that fan their children out: 0 sequential,
    /// 1 root only, 2 root plus its immediate children.
    /// </param>
    public ScanTreeBudget(
        int maxRetainedNodes = 100_000,
        int maxChildrenPerDirectory = 99,
        int maxRetainedDepth = 6,
        int maxInaccessiblePaths = 10_000,
        int maxDegreeOfParallelism = 0,
        int parallelFanOutLevels = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedNodes, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChildrenPerDirectory, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedDepth, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInaccessiblePaths);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDegreeOfParallelism);
        ArgumentOutOfRangeException.ThrowIfNegative(parallelFanOutLevels);

        MaxRetainedNodes = maxRetainedNodes;
        MaxChildrenPerDirectory = maxChildrenPerDirectory;
        MaxRetainedDepth = maxRetainedDepth;
        MaxInaccessiblePaths = maxInaccessiblePaths;
        MaxDegreeOfParallelism = maxDegreeOfParallelism == 0
            ? Math.Min(Environment.ProcessorCount, 16)
            : maxDegreeOfParallelism;
        ParallelFanOutLevels = parallelFanOutLevels;
    }

    public int MaxRetainedNodes { get; }
    public int MaxChildrenPerDirectory { get; }
    public int MaxRetainedDepth { get; }
    public int MaxInaccessiblePaths { get; }
    public int MaxDegreeOfParallelism { get; }
    public int ParallelFanOutLevels { get; }
```

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~ScanTreeBudgetTests"`
Expected: PASS.

- [ ] **Step 5: Run the full ScannerCore suite**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug`
Expected: PASS — the DOP default rose from 4 to `Min(ProcessorCount, 16)`, which existing tests tolerate because they pass explicit budgets where concurrency matters.

- [ ] **Step 6: Commit**

```bash
git add ScannerCore/ScanTreeBudget.cs ScannerCore.Tests/ScanTreeBudgetTests.cs
git commit -m "feat: add ParallelFanOutLevels and auto degree to scan budget"
```

---

### Task 2: Hierarchical synthetic directory source for tests

`SyntheticDirectoryEntrySource` serves one flat directory and returns `null` for every non-root path, so it cannot express a tree. Every later task needs a tree it can control.

**Files:**
- Create: `ScannerCore.Tests/SyntheticTreeSource.cs`
- Modify: `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`

**Interfaces:**
- Produces: `SyntheticNode.Dir(string, params SyntheticNode[])`, `SyntheticNode.File(string, long)`, `SyntheticTreeSource(string rootPath, SyntheticNode root)` with `ConcurrencyProbe Probe { get; }`, `Action<string>? GateRead { get; set; }`, `string? FailPath { get; set; }`; `ConcurrencyProbe.Peak`; `Rendezvous(int participants, TimeSpan timeout)` with `Arrive()` and `Peak`.
- Consumes: `IDirectoryEntrySource`, `IDirectoryEntryCursor`, `IDirectoryEntrySink`, `DirectoryBatchResult` (all `internal` to `ScannerCore`, already visible to the test project).

- [ ] **Step 1: Write the failing test**

Add to `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`:

```csharp
    [Fact]
    public void Synthetic_tree_source_walks_a_nested_tree()
    {
        var tree = SyntheticNode.Dir("root",
            SyntheticNode.Dir("a",
                SyntheticNode.File("f1", 10),
                SyntheticNode.Dir("b", SyntheticNode.File("f2", 20))),
            SyntheticNode.File("f3", 5));
        var source = new SyntheticTreeSource(@"C:\root", tree);

        var result = new BoundedDirectoryWalker(source).Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 1),
            CancellationToken.None,
            null);

        Assert.Equal(35, result.Total);
        Assert.Equal(35, result.Root.Size);
        Assert.Equal(0, result.InaccessibleCount);
        Assert.True(source.Probe.Peak >= 1);
    }
```

- [ ] **Step 2: Run the test and verify it fails**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~Synthetic_tree_source_walks_a_nested_tree"`
Expected: compile error — `SyntheticNode` and `SyntheticTreeSource` do not exist.

- [ ] **Step 3: Create the source**

Create `ScannerCore.Tests/SyntheticTreeSource.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ScannerCore;

namespace ScannerCore.Tests;

internal sealed class SyntheticNode
{
    public required string Name { get; init; }
    public long Size { get; init; }
    public bool IsDirectory { get; init; }
    public List<SyntheticNode> Children { get; } = [];

    public static SyntheticNode File(string name, long size) =>
        new() { Name = name, Size = size, IsDirectory = false };

    public static SyntheticNode Dir(string name, params SyntheticNode[] children)
    {
        var node = new SyntheticNode { Name = name, Size = 0, IsDirectory = true };
        node.Children.AddRange(children);
        return node;
    }
}

/// <summary>Tracks how many directory reads are in flight across the whole source.</summary>
internal sealed class ConcurrencyProbe
{
    private int _current;
    private int _peak;

    public int Peak => Volatile.Read(ref _peak);

    public IDisposable Enter()
    {
        var now = Interlocked.Increment(ref _current);
        RecordPeak(ref _peak, now);
        return new Scope(this);
    }

    internal static void RecordPeak(ref int peak, int candidate)
    {
        var observed = Volatile.Read(ref peak);
        while (candidate > observed)
        {
            var prior = Interlocked.CompareExchange(ref peak, candidate, observed);
            if (prior == observed)
                break;
            observed = prior;
        }
    }

    private sealed class Scope(ConcurrencyProbe owner) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref owner._current);
    }
}

/// <summary>
/// A meeting point for a specific set of directories. Each participant blocks inside its
/// read until the expected number of participants has arrived or the timeout expires, so
/// "were these two directories walked at the same time?" becomes a deterministic
/// assertion instead of a wall-clock comparison. Unlike <see cref="ConcurrencyProbe"/>
/// this counts only the directories a test opts in, so unrelated overlapping reads (a
/// parent's trailing end-of-directory read, for instance) cannot inflate the result.
/// </summary>
internal sealed class Rendezvous(int participants, TimeSpan timeout)
{
    private int _current;
    private int _peak;

    public int Peak => Volatile.Read(ref _peak);

    public void Arrive()
    {
        var now = Interlocked.Increment(ref _current);
        ConcurrencyProbe.RecordPeak(ref _peak, now);

        var elapsed = Stopwatch.StartNew();
        while (Volatile.Read(ref _current) < participants && elapsed.Elapsed < timeout)
            Thread.Sleep(5);

        Interlocked.Decrement(ref _current);
    }
}

internal sealed class SyntheticTreeSource : IDirectoryEntrySource
{
    private readonly Dictionary<string, SyntheticNode> _byPath;

    public SyntheticTreeSource(string rootPath, SyntheticNode root)
    {
        _byPath = new Dictionary<string, SyntheticNode>(StringComparer.OrdinalIgnoreCase);
        Index(rootPath, root);
    }

    public ConcurrencyProbe Probe { get; } = new();

    /// <summary>Invoked at the start of every read, before entries are produced.</summary>
    public Action<string>? GateRead { get; set; }

    /// <summary>Reads of this directory return <see cref="DirectoryBatchResult.Failed"/>.</summary>
    public string? FailPath { get; set; }

    public IDirectoryEntryCursor? Open(string path)
    {
        var normalized = Normalize(path);
        return _byPath.TryGetValue(normalized, out var node) && node.IsDirectory
            ? new Cursor(node, normalized, this)
            : null;
    }

    private void Index(string path, SyntheticNode node)
    {
        _byPath[Normalize(path)] = node;
        foreach (var child in node.Children)
            Index(Path.Combine(path, child.Name), child);
    }

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar);

    private sealed class Cursor(
        SyntheticNode node,
        string path,
        SyntheticTreeSource owner) : IDirectoryEntryCursor
    {
        private int _index;

        public DirectoryBatchResult ReadNext(Span<byte> buffer, IDirectoryEntrySink sink)
        {
            using var scope = owner.Probe.Enter();
            owner.GateRead?.Invoke(path);

            if (string.Equals(path, owner.FailPath, StringComparison.OrdinalIgnoreCase))
                return DirectoryBatchResult.Failed;

            if (_index >= node.Children.Count)
                return DirectoryBatchResult.Completed;

            var end = Math.Min(node.Children.Count, _index + 256);
            for (; _index < end; _index++)
            {
                var child = node.Children[_index];
                sink.OnEntry(child.Name.AsSpan(), child.Size, child.IsDirectory);
            }

            return DirectoryBatchResult.Entries;
        }

        public void Dispose() { }
    }
}
```

- [ ] **Step 4: Run the test and verify it passes**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~Synthetic_tree_source_walks_a_nested_tree"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ScannerCore.Tests/SyntheticTreeSource.cs ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs
git commit -m "test: add hierarchical synthetic directory source with concurrency probe"
```

---

### Task 3: Rewrite the walker as async-through fan-out with slot discipline

Replaces the per-call-site channel/worker pool with one scan-wide `SemaphoreSlim`, makes the fan-out levels async, and streams children through a bounded window. Behavior stays root-only because `ParallelFanOutLevels` still defaults to `1`.

**Files:**
- Modify: `ScannerCore/BoundedDirectoryWalker.cs` (whole file)
- Modify: `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`

**Interfaces:**
- Consumes: `ScanTreeBudget.ParallelFanOutLevels`, `ScanTreeBudget.MaxDegreeOfParallelism` (Task 1); `SyntheticTreeSource`, `ConcurrencyProbe` (Task 2).
- Produces: unchanged public surface — `BoundedDirectoryWalker(IDirectoryEntrySource source, bool parallelizeTopLevel = false)` and `ScanResult Scan(string target, ScanTreeBudget budget, CancellationToken token, Action<string, long>? onProgress)`.
- Removes: `WalkChildrenInParallel`, `RunChildWorkersAsync`, `RunWorkerAsync`, `DirectoryWorkItem`, and the `System.Threading.Channels` dependency.

- [ ] **Step 1: Write the tests**

The first test is the real driver: nothing today consults `ParallelFanOutLevels`, so second-level directories cannot overlap and it fails. The other two are characterization tests — they must pass both before and after, because they lock in the invariants the rewrite has to preserve.

Add to `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`:

```csharp
    private static SyntheticNode LongPoleTree() =>
        SyntheticNode.Dir("root",
            SyntheticNode.Dir("only",
                SyntheticNode.Dir("g1", SyntheticNode.File("a", 1)),
                SyntheticNode.Dir("g2", SyntheticNode.File("b", 2))));

    private static bool IsGrandchild(string path) =>
        path.EndsWith("g1", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("g2", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="onFirst"/> the first time each matching directory is read, so a
    /// rendezvous is not re-entered by a directory's trailing end-of-directory read.
    /// </summary>
    private static Action<string> GateFirstRead(
        Func<string, bool> match, Action onFirst)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return path =>
        {
            if (!match(path))
                return;
            lock (seen)
            {
                if (!seen.Add(path))
                    return;
            }

            onFirst();
        };
    }

    private static bool CompletesWithin(Task task, TimeSpan timeout) =>
        ReferenceEquals(
            Task.WhenAny(task, Task.Delay(timeout)).GetAwaiter().GetResult(),
            task);

    [Fact]
    public void Second_level_subtrees_are_walked_concurrently()
    {
        var rendezvous = new Rendezvous(participants: 2, TimeSpan.FromSeconds(3));
        var source = new SyntheticTreeSource(@"C:\root", LongPoleTree())
        {
            GateRead = GateFirstRead(IsGrandchild, rendezvous.Arrive)
        };

        var result = new BoundedDirectoryWalker(source, parallelizeTopLevel: true).Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 2, parallelFanOutLevels: 2),
            CancellationToken.None,
            null);

        Assert.Equal(3, result.Total);
        Assert.Equal(2, rendezvous.Peak);
    }

    [Fact]
    public void Concurrent_reads_never_exceed_the_configured_degree()
    {
        var children = new List<SyntheticNode>();
        for (var d = 0; d < 16; d++)
            children.Add(SyntheticNode.Dir(
                $"dir{d}",
                SyntheticNode.File("f", d + 1),
                SyntheticNode.Dir("nested", SyntheticNode.File("g", d + 1))));

        var source = new SyntheticTreeSource(@"C:\root", SyntheticNode.Dir("root", [.. children]))
        {
            GateRead = _ => Thread.Sleep(2)
        };

        new BoundedDirectoryWalker(source, parallelizeTopLevel: true).Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 3, parallelFanOutLevels: 1),
            CancellationToken.None,
            null);

        Assert.True(source.Probe.Peak <= 3, $"peak concurrent reads was {source.Probe.Peak}");
    }

    [Fact]
    public void Single_slot_fan_out_completes_without_deadlock()
    {
        var tree = SyntheticNode.Dir("root",
            SyntheticNode.Dir("a", SyntheticNode.File("f", 1)),
            SyntheticNode.Dir("b", SyntheticNode.File("f", 2)));
        var source = new SyntheticTreeSource(@"C:\root", tree);
        var walker = new BoundedDirectoryWalker(source, parallelizeTopLevel: true);

        var scan = Task.Run(() => walker.Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 1, parallelFanOutLevels: 2),
            CancellationToken.None,
            null));

        Assert.True(CompletesWithin(scan, TimeSpan.FromSeconds(15)), "scan did not complete");
        Assert.Equal(3, scan.Result.Total);
    }
```

Add `using System.Collections.Generic;` and `using System.Threading.Tasks;` to the test file if missing.

- [ ] **Step 2: Run the tests and confirm which fail**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~DirectoryWalkEngineParallelTests"`
Expected: `Second_level_subtrees_are_walked_concurrently` FAILS with `Expected: 2, Actual: 1` after roughly six seconds of rendezvous timeouts, because the current walker ignores `ParallelFanOutLevels` and walks `only` as one sequential subtree. `Concurrent_reads_never_exceed_the_configured_degree` and `Single_slot_fan_out_completes_without_deadlock` PASS — they describe invariants the rewrite must not break.

- [ ] **Step 3: Rewrite the walker**

Replace the entire contents of `ScannerCore/BoundedDirectoryWalker.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ScannerCore;

/// <summary>
/// One-pass bounded post-order walker: computes exact totals while retaining only a
/// depth/width/count-bounded subset of the tree per <see cref="ScanTreeBudget"/>.
/// Directories above <see cref="ScanTreeBudget.ParallelFanOutLevels"/> fan their children
/// out; everything below walks its whole subtree synchronously on one slot of a scan-wide
/// semaphore. A slot is held only around a native read or a sequential subtree, never
/// across a wait for children, so no wait cycle can form.
/// </summary>
internal sealed class BoundedDirectoryWalker(
    IDirectoryEntrySource source,
    bool parallelizeTopLevel = false)
{
    private readonly IDirectoryEntrySource _source = source;
    private readonly bool _parallelizeTopLevel = parallelizeTopLevel;

    internal ScanResult Scan(
        string target,
        ScanTreeBudget budget,
        CancellationToken token,
        Action<string, long>? onProgress)
    {
        var degree = _parallelizeTopLevel
            ? Math.Max(1, budget.MaxDegreeOfParallelism)
            : 1;
        var levels = _parallelizeTopLevel ? budget.ParallelFanOutLevels : 0;

        var context = new WalkContext(
            _source, budget, levels, degree, token, onProgress);

        // The only blocking wait in the walk, on the caller's own thread.
        var root = WalkAsync(
                target, target, 0, budget.MaxRetainedNodes, context)
            .GetAwaiter().GetResult();

        var inaccessible = context.SnapshotInaccessible();
        var inaccessibleCount = context.InaccessibleCount;
        return new ScanResult
        {
            Root = root,
            Total = context.Total,
            Inaccessible = inaccessible,
            InaccessibleCount = inaccessibleCount,
            InaccessiblePathsTruncated =
                inaccessibleCount > inaccessible.Length
        };
    }

    private async Task<FsItem> WalkAsync(
        string path,
        string name,
        int depth,
        int allowance,
        WalkContext context)
    {
        if (depth < context.FanOutLevels)
            return await WalkFanOutAsync(path, name, depth, allowance, context)
                .ConfigureAwait(false);

        await context.AcquireSlotAsync().ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(DirectoryScanner.BufferSize);
        try
        {
            return WalkSequential(path, name, depth, allowance, buffer, context);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            context.ReleaseSlot();
        }
    }

    private FsItem WalkSequential(
        string path,
        string name,
        int depth,
        int allowance,
        byte[] buffer,
        WalkContext context)
    {
        context.Token.ThrowIfCancellationRequested();
        context.Report(path);

        var item = new FsItem(name, 0, isDir: true);
        using var cursor = context.Source.Open(EnsureTrailingSeparator(path));
        if (cursor is null)
        {
            item.Items = null;
            context.AddInaccessible(path);
            return item;
        }

        var partition = DividePartition(depth, allowance, context);
        var collector = new BoundedChildCollector(partition.Slots);
        var total = WalkChildrenSequentially(
            cursor, path, depth, buffer, partition, collector, context);
        return Complete(item, collector, allowance, total);
    }

    private long WalkChildrenSequentially(
        IDirectoryEntryCursor cursor,
        string path,
        int depth,
        byte[] buffer,
        ChildAllowance partition,
        BoundedChildCollector collector,
        WalkContext context)
    {
        long total = 0;
        // One sink for the whole directory, reset per batch: recursing batch by batch
        // keeps only one batch of subdirectory names alive, which is what bounds memory
        // on directories with millions of entries.
        var sink = new BatchSink(collector);
        while (true)
        {
            context.Token.ThrowIfCancellationRequested();
            sink.Reset();
            var status = cursor.ReadNext(buffer, sink);
            total = checked(total + sink.FileSize);
            context.AddToTotal(sink.FileSize);

            foreach (var childName in sink.Directories)
            {
                var child = WalkSequential(
                    Path.Combine(path, childName),
                    childName,
                    depth + 1,
                    partition.NodesPerChild,
                    buffer,
                    context);
                total = checked(total + child.Size);
                collector.ConsiderDirectory(child);
            }

            if (status == DirectoryBatchResult.Completed)
                break;
            if (status == DirectoryBatchResult.Failed)
                throw new IOException(
                    $"Native directory enumeration failed for '{path}'.");
        }

        return total;
    }

    /// <summary>
    /// Reads <paramref name="path"/> one batch at a time under a slot and schedules each
    /// discovered subdirectory into a bounded in-flight window, draining completed children
    /// into the collector as it goes. The slot is released before any wait on a child, and
    /// the window keeps the number of live subdirectory names bounded regardless of how
    /// many entries the directory holds.
    /// </summary>
    private async Task<FsItem> WalkFanOutAsync(
        string path,
        string name,
        int depth,
        int allowance,
        WalkContext context)
    {
        context.Token.ThrowIfCancellationRequested();
        context.Report(path);

        var item = new FsItem(name, 0, isDir: true);
        using var cursor = context.Source.Open(EnsureTrailingSeparator(path));
        if (cursor is null)
        {
            item.Items = null;
            context.AddInaccessible(path);
            return item;
        }

        var partition = DividePartition(depth, allowance, context);
        var collector = new BoundedChildCollector(partition.Slots);
        var sink = new BatchSink(collector);
        var pending = new List<Task<FsItem>>(context.ChildWindow);

        long total = 0;
        var completed = false;
        while (!completed)
        {
            context.Token.ThrowIfCancellationRequested();
            completed = await ReadBatchAsync(cursor, path, sink, context)
                .ConfigureAwait(false);
            total = checked(total + sink.FileSize);

            foreach (var childName in sink.Directories)
            {
                while (pending.Count >= context.ChildWindow)
                    total = checked(total + await DrainOneAsync(pending, collector)
                        .ConfigureAwait(false));

                pending.Add(WalkAsync(
                    Path.Combine(path, childName),
                    childName,
                    depth + 1,
                    partition.NodesPerChild,
                    context));
            }
        }

        while (pending.Count > 0)
            total = checked(total + await DrainOneAsync(pending, collector)
                .ConfigureAwait(false));

        return Complete(item, collector, allowance, total);
    }

    /// <summary>
    /// Reads one native batch while holding a slot, and returns whether the directory is
    /// exhausted. The pooled buffer is rented and returned inside the slot, so outstanding
    /// rentals never exceed the configured degree.
    /// </summary>
    private static async Task<bool> ReadBatchAsync(
        IDirectoryEntryCursor cursor,
        string path,
        BatchSink sink,
        WalkContext context)
    {
        await context.AcquireSlotAsync().ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(DirectoryScanner.BufferSize);
        try
        {
            sink.Reset();
            var status = cursor.ReadNext(buffer, sink);
            context.AddToTotal(sink.FileSize);
            if (status == DirectoryBatchResult.Failed)
                throw new IOException(
                    $"Native directory enumeration failed for '{path}'.");
            return status == DirectoryBatchResult.Completed;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            context.ReleaseSlot();
        }
    }

    private static async Task<long> DrainOneAsync(
        List<Task<FsItem>> pending,
        BoundedChildCollector collector)
    {
        var finished = await Task.WhenAny(pending).ConfigureAwait(false);
        pending.Remove(finished);
        var child = await finished.ConfigureAwait(false);
        collector.ConsiderDirectory(child);
        return child.Size;
    }

    private static FsItem Complete(
        FsItem item,
        BoundedChildCollector collector,
        int allowance,
        long total)
    {
        item.HasUnretainedChildren = collector.HasHiddenChildren;
        // An allowance of one leaves no room even for an aggregate child, so the
        // directory retains nothing; HasUnretainedChildren still keeps it scopable.
        item.AttachChildren(allowance <= 1 ? [] : collector.BuildChildren());
        item.Size = total;
        return item;
    }

    private static ChildAllowance DividePartition(
        int depth,
        int allowance,
        WalkContext context) =>
        depth + 1 >= context.Budget.MaxRetainedDepth
            ? new ChildAllowance(0, 0)
            : DivideAllowance(allowance, context.Budget.MaxChildrenPerDirectory);

    private static ChildAllowance DivideAllowance(
        int allowance,
        int maxChildren)
    {
        if (allowance < 3)
            return new(0, 0);

        var slots = Math.Min(maxChildren, allowance - 3);
        var nodesPerChild = Math.Max(
            1, (allowance - 2) / (slots + 1));
        return new(slots, nodesPerChild);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private readonly record struct ChildAllowance(
        int Slots,
        int NodesPerChild);

    /// <summary>
    /// Collects one native batch: files go straight into the collector, subdirectory names
    /// are handed back to the walker. <see cref="Reset"/> before each batch so
    /// <see cref="FileSize"/> stays a per-batch delta.
    /// </summary>
    private sealed class BatchSink(
        BoundedChildCollector collector) : IDirectoryEntrySink
    {
        public List<string> Directories { get; } = [];
        public long FileSize { get; private set; }

        public void Reset()
        {
            Directories.Clear();
            FileSize = 0;
        }

        public void OnEntry(
            ReadOnlySpan<char> name,
            long size,
            bool isDirectory)
        {
            if (isDirectory)
            {
                Directories.Add(name.ToString());
                return;
            }

            FileSize = checked(FileSize + size);
            collector.ConsiderFile(name, size);
        }
    }

    private sealed class WalkContext
    {
        private readonly List<string> _inaccessible = [];
        private readonly object _inaccessibleLock = new();
        private readonly SemaphoreSlim _slots;
        private long _total;
        private long _inaccessibleCount;

        public WalkContext(
            IDirectoryEntrySource source,
            ScanTreeBudget budget,
            int fanOutLevels,
            int degree,
            CancellationToken token,
            Action<string, long>? onProgress)
        {
            Source = source;
            Budget = budget;
            FanOutLevels = fanOutLevels;
            ChildWindow = Math.Max(2, 2 * degree);
            Token = token;
            OnProgress = onProgress;
            _slots = new SemaphoreSlim(degree, degree);
        }

        public IDirectoryEntrySource Source { get; }
        public ScanTreeBudget Budget { get; }
        public int FanOutLevels { get; }
        public int ChildWindow { get; }
        public CancellationToken Token { get; }
        private Action<string, long>? OnProgress { get; }
        public long Total => Interlocked.Read(ref _total);
        public long InaccessibleCount => Interlocked.Read(ref _inaccessibleCount);

        // The semaphore is deliberately never disposed: it holds no wait handle (we never
        // touch AvailableWaitHandle), and disposing it could fault a straggling task.
        public Task AcquireSlotAsync() => _slots.WaitAsync(Token);

        public void ReleaseSlot() => _slots.Release();

        public void AddToTotal(long size) =>
            Interlocked.Add(ref _total, size);

        public string[] SnapshotInaccessible()
        {
            lock (_inaccessibleLock)
                return _inaccessible.ToArray();
        }

        public void AddInaccessible(string path)
        {
            Interlocked.Increment(ref _inaccessibleCount);
            lock (_inaccessibleLock)
            {
                if (_inaccessible.Count < Budget.MaxInaccessiblePaths)
                    _inaccessible.Add(path);
            }
        }

        public void Report(string path) =>
            OnProgress?.Invoke(path, Total);
    }
}
```

- [ ] **Step 4: Run the new tests and verify they pass**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~DirectoryWalkEngineParallelTests"`
Expected: PASS, including the pre-existing equivalence, cancellation, and spinning-disk tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ScannerCore/BoundedDirectoryWalker.cs ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs
git commit -m "refactor: walk fan-out levels asynchronously under one shared slot budget"
```

---

### Task 4: Abort siblings on first failure and preserve the original exception

Without this, one failing subtree leaves its siblings walking to completion, and the failure can surface as a derived `OperationCanceledException` instead of the real `IOException`.

**Files:**
- Modify: `ScannerCore/BoundedDirectoryWalker.cs`
- Modify: `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`

**Interfaces:**
- Consumes: `SyntheticTreeSource.FailPath` (Task 2), the walker structure from Task 3.
- Produces: no public surface change. Internally `WalkContext` gains `Fail(Exception)`, `Failure`, `DisposeAbort()`, and a linked `CancellationTokenSource`; the walker gains `ObserveAsync(List<Task<FsItem>>)`.

- [ ] **Step 1: Write the failing tests**

Add to `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`:

```csharp
    [Fact]
    public void Read_failure_in_one_subtree_aborts_the_scan_with_io_exception()
    {
        var tree = SyntheticNode.Dir("root",
            SyntheticNode.Dir("good", SyntheticNode.File("f", 1)),
            SyntheticNode.Dir("bad", SyntheticNode.File("f", 2)));
        var source = new SyntheticTreeSource(@"C:\root", tree)
        {
            FailPath = @"C:\root\bad"
        };
        var walker = new BoundedDirectoryWalker(source, parallelizeTopLevel: true);

        var scan = Task.Run(() => walker.Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 2, parallelFanOutLevels: 2),
            CancellationToken.None,
            null));

        Assert.True(CompletesWithin(scan, TimeSpan.FromSeconds(15)), "scan did not complete");
        Assert.IsType<IOException>(scan.Exception!.InnerException);
    }

    [Fact]
    public void Cancellation_during_fan_out_throws_operation_canceled()
    {
        var children = new List<SyntheticNode>();
        for (var d = 0; d < 32; d++)
            children.Add(SyntheticNode.Dir($"dir{d}", SyntheticNode.File("f", 1)));

        using var cts = new CancellationTokenSource();
        var source = new SyntheticTreeSource(@"C:\root", SyntheticNode.Dir("root", [.. children]))
        {
            GateRead = _ => cts.Cancel()
        };
        var walker = new BoundedDirectoryWalker(source, parallelizeTopLevel: true);

        var scan = Task.Run(() => walker.Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 2, parallelFanOutLevels: 2),
            cts.Token,
            null));

        Assert.True(CompletesWithin(scan, TimeSpan.FromSeconds(15)), "scan did not complete");
        Assert.IsAssignableFrom<OperationCanceledException>(scan.Exception!.InnerException);
    }
```

Add `using System.IO;` to the test file if missing.

- [ ] **Step 2: Run the tests and verify the failure test is unreliable or wrong**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~Read_failure_in_one_subtree_aborts_the_scan_with_io_exception"`
Expected: FAIL or flaky — the sibling walk keeps running and the surfaced exception type is not guaranteed.

- [ ] **Step 3: Add failure capture and sibling abort**

In `WalkContext`, replace the token plumbing:

```csharp
        private readonly CancellationTokenSource _abort;
        private Exception? _failure;

        public WalkContext(
            IDirectoryEntrySource source,
            ScanTreeBudget budget,
            int fanOutLevels,
            int degree,
            CancellationToken token,
            Action<string, long>? onProgress)
        {
            Source = source;
            Budget = budget;
            FanOutLevels = fanOutLevels;
            ChildWindow = Math.Max(2, 2 * degree);
            _abort = CancellationTokenSource.CreateLinkedTokenSource(token);
            Token = _abort.Token;
            OnProgress = onProgress;
            _slots = new SemaphoreSlim(degree, degree);
        }

        public CancellationToken Token { get; }
        public Exception? Failure => Volatile.Read(ref _failure);

        /// <summary>
        /// Records the first real failure and cancels the linked token so sibling subtrees
        /// stop instead of walking to completion behind a doomed scan.
        /// </summary>
        public void Fail(Exception failure)
        {
            if (Interlocked.CompareExchange(ref _failure, failure, null) is null)
                _abort.Cancel();
        }

        public void DisposeAbort() => _abort.Dispose();
```

In `WalkAsync`, report sequential failures:

```csharp
        await context.AcquireSlotAsync().ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(DirectoryScanner.BufferSize);
        try
        {
            return WalkSequential(path, name, depth, allowance, buffer, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Fail(ex);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            context.ReleaseSlot();
        }
```

In `WalkFanOutAsync`, wrap the read/schedule/drain region so in-flight children are always observed:

```csharp
        long total = 0;
        try
        {
            var completed = false;
            while (!completed)
            {
                context.Token.ThrowIfCancellationRequested();
                completed = await ReadBatchAsync(cursor, path, sink, context)
                    .ConfigureAwait(false);
                total = checked(total + sink.FileSize);

                foreach (var childName in sink.Directories)
                {
                    while (pending.Count >= context.ChildWindow)
                        total = checked(total + await DrainOneAsync(pending, collector)
                            .ConfigureAwait(false));

                    pending.Add(WalkAsync(
                        Path.Combine(path, childName),
                        childName,
                        depth + 1,
                        partition.NodesPerChild,
                        context));
                }
            }

            while (pending.Count > 0)
                total = checked(total + await DrainOneAsync(pending, collector)
                    .ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                context.Fail(ex);
            await ObserveAsync(pending).ConfigureAwait(false);
            throw;
        }

        return Complete(item, collector, allowance, total);
```

Add the observer:

```csharp
    /// <summary>
    /// Drains in-flight children after a failure so none of them ends up as an unobserved
    /// task exception. The first real failure is already captured on the context.
    /// </summary>
    private static async Task ObserveAsync(List<Task<FsItem>> pending)
    {
        foreach (var task in pending)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Secondary failures and derived cancellations are expected here.
            }
        }

        pending.Clear();
    }
```

In `Scan`, surface the captured failure instead of a derived cancellation, and dispose the linked source once every task is terminal:

```csharp
        var context = new WalkContext(
            _source, budget, levels, degree, token, onProgress);

        FsItem? root = null;
        ExceptionDispatchInfo? captured = null;
        try
        {
            // The only blocking wait in the walk, on the caller's own thread.
            root = WalkAsync(
                    target, target, 0, budget.MaxRetainedNodes, context)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (
            !token.IsCancellationRequested && context.Failure is { } failure)
        {
            captured = ExceptionDispatchInfo.Capture(failure);
        }
        finally
        {
            context.DisposeAbort();
        }

        captured?.Throw();

        var inaccessible = context.SnapshotInaccessible();
        var inaccessibleCount = context.InaccessibleCount;
        return new ScanResult
        {
            Root = root!,
            Total = context.Total,
            Inaccessible = inaccessible,
            InaccessibleCount = inaccessibleCount,
            InaccessiblePathsTruncated =
                inaccessibleCount > inaccessible.Length
        };
```

Add `using System.Runtime.ExceptionServices;` to the walker.

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~DirectoryWalkEngineParallelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ScannerCore/BoundedDirectoryWalker.cs ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs
git commit -m "fix: abort sibling subtrees on first walk failure and preserve its exception"
```

---

### Task 5: Make two-level fan-out the default

Task 3 made the levels knob work and proved second-level concurrency. This task turns it on by default, pins the negative control, and proves the retained tree is invariant across every configuration.

**Files:**
- Modify: `ScannerCore/ScanTreeBudget.cs` (default `parallelFanOutLevels`)
- Modify: `ScannerCore.Tests/ScanTreeBudgetTests.cs`
- Modify: `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–4, including `LongPoleTree()`, `IsGrandchild(string)`, and `GateFirstRead(Func<string, bool>, Action)` added to the test file in Task 3.
- Produces: `ScanTreeBudget.ParallelFanOutLevels` default `2`.

- [ ] **Step 1: Write the failing tests**

Add to `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`:

```csharp
    [Fact]
    public void Root_only_fan_out_leaves_second_level_subtrees_serialized()
    {
        var rendezvous = new Rendezvous(participants: 2, TimeSpan.FromMilliseconds(200));
        var source = new SyntheticTreeSource(@"C:\root", LongPoleTree())
        {
            GateRead = GateFirstRead(IsGrandchild, rendezvous.Arrive)
        };

        new BoundedDirectoryWalker(source, parallelizeTopLevel: true).Scan(
            @"C:\root",
            new ScanTreeBudget(maxDegreeOfParallelism: 2, parallelFanOutLevels: 1),
            CancellationToken.None,
            null);

        Assert.Equal(1, rendezvous.Peak);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    public void Retained_tree_is_identical_across_fan_out_configurations(
        int levels, int degree)
    {
        var children = new List<SyntheticNode>();
        for (var d = 0; d < 8; d++)
        {
            var grandchildren = new List<SyntheticNode>();
            for (var g = 0; g < 5; g++)
                grandchildren.Add(SyntheticNode.Dir(
                    $"g{g}",
                    SyntheticNode.File("leaf", (d * 5) + g + 1)));
            children.Add(SyntheticNode.Dir($"dir{d}", [.. grandchildren]));
        }

        var tree = SyntheticNode.Dir("root", [.. children]);
        var budget = new ScanTreeBudget(
            maxRetainedNodes: 500,
            maxChildrenPerDirectory: 4,
            maxRetainedDepth: 6,
            maxDegreeOfParallelism: degree,
            parallelFanOutLevels: levels);

        var expected = new BoundedDirectoryWalker(
                new SyntheticTreeSource(@"C:\root", tree))
            .Scan(@"C:\root", new ScanTreeBudget(
                    maxRetainedNodes: 500,
                    maxChildrenPerDirectory: 4,
                    maxRetainedDepth: 6,
                    maxDegreeOfParallelism: 1,
                    parallelFanOutLevels: 0),
                CancellationToken.None, null);

        var actual = new BoundedDirectoryWalker(
                new SyntheticTreeSource(@"C:\root", tree),
                parallelizeTopLevel: true)
            .Scan(@"C:\root", budget, CancellationToken.None, null);

        Assert.Equal(expected.Total, actual.Total);
        Assert.Equal(expected.InaccessibleCount, actual.InaccessibleCount);
        AssertSameTree(expected.Root, actual.Root);
    }

    private static void AssertSameTree(FsItem expected, FsItem actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.HasUnretainedChildren, actual.HasUnretainedChildren);
        Assert.Equal(expected.Items is null, actual.Items is null);
        if (expected.Items is null)
            return;

        Assert.Equal(expected.Items.Count, actual.Items!.Count);
        for (var i = 0; i < expected.Items.Count; i++)
            AssertSameTree(expected.Items[i], actual.Items[i]);
    }
```

Add to `ScannerCore.Tests/ScanTreeBudgetTests.cs`, replacing `Budget_defaults_to_root_only_fan_out`:

```csharp
    [Fact]
    public void Budget_defaults_to_two_fan_out_levels()
    {
        Assert.Equal(2, new ScanTreeBudget().ParallelFanOutLevels);
    }
```

- [ ] **Step 2: Run the tests and confirm which fail**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter "FullyQualifiedName~ScanTreeBudgetTests|FullyQualifiedName~DirectoryWalkEngineParallelTests"`
Expected: `Budget_defaults_to_two_fan_out_levels` FAILS with `Expected: 2, Actual: 1`. `Root_only_fan_out_leaves_second_level_subtrees_serialized` and every `Retained_tree_is_identical_across_fan_out_configurations` case PASS, since they all pass explicit budgets.

- [ ] **Step 3: Flip the default**

In `ScannerCore/ScanTreeBudget.cs`, change the constructor default:

```csharp
        int parallelFanOutLevels = 2)
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release`
Expected: PASS, including `BoundedScanMemoryTests` and `BoundedDirectoryWalkerTests`.

- [ ] **Step 5: Commit**

```bash
git add ScannerCore/ScanTreeBudget.cs ScannerCore.Tests/ScanTreeBudgetTests.cs ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs
git commit -m "feat: fan out two levels by default and pin cross-configuration equivalence"
```

---

### Task 6: Measure on a real volume and lock the defaults

**Files:**
- Modify: `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs:71-83`
- Modify: `ScannerCore/ScanTreeBudget.cs` (only if measurement says so)

**Interfaces:**
- Consumes: `ScanTreeBudget(maxDegreeOfParallelism:, parallelFanOutLevels:)`.
- Produces: `MeasureScan(DirectoryWalkEngine engine, ScanTreeBudget budget, out long total)`.

- [ ] **Step 1: Parameterize the harness by budget**

In `DirectoryWalkEngineParallelSpeedTests`, change the helper signature and its two call sites in `Parallel_walk_is_faster_than_sequential_on_c_drive` to pass `ScanTreeBudget.Default`:

```csharp
    private static TimeSpan MeasureScan(
        DirectoryWalkEngine engine, ScanTreeBudget budget, out long total)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var stopwatch = Stopwatch.StartNew();
        var result = engine.Scan(
            MeasurementRoot, isDriveScan: true, CancellationToken.None,
            onProgress: null, budget);
        stopwatch.Stop();

        total = result.Total;
        return stopwatch.Elapsed;
    }
```

- [ ] **Step 2: Add the reporting matrix**

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

        foreach (var config in configs)
        {
            var engine = new DirectoryWalkEngine(_ => config.Levels > 0);
            var budget = new ScanTreeBudget(
                maxDegreeOfParallelism: config.Degree,
                parallelFanOutLevels: config.Levels);

            var samples = new List<TimeSpan>(capacity: 2);
            long total = 0;
            for (var round = 0; round < 2; round++)
                samples.Add(MeasureScan(engine, budget, out total));

            output.WriteLine(
                $"{config.Name,-20} levels={config.Levels} dop={config.Degree,-2} " +
                $"median={Median(samples).TotalSeconds:F2}s total={total:N0}");
        }
    }
```

- [ ] **Step 3: Run the matrix on the NVMe machine**

Run:

```powershell
$env:SIZESCANNER_RUN_PERF_TESTS = "1"
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~Fan_out_configuration_matrix_report" --logger "console;verbosity=detailed"
```

Expected: five median timings printed, and identical `total` values across configs. Differing totals mean a correctness bug, not a tuning result — stop and investigate.

- [ ] **Step 4: Record the numbers and set the defaults**

Paste the five medians into the spec under a new `## Measured results` section with the machine's CPU and drive model. Then apply the decision rule:

- D materially faster than B and C, E within noise of D → keep `parallelFanOutLevels: 2` and `maxDegreeOfParallelism: 0` (auto).
- D ≈ C → set the default back to `parallelFanOutLevels: 1` and keep the auto degree; the two-level path stays available as a knob.
- E materially faster than D → leave the default at 2 and add a note that real work-stealing is worth a follow-up design.

- [ ] **Step 5: Commit**

```bash
git add ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs ScannerCore/ScanTreeBudget.cs docs/superpowers/specs/2026-08-06-shallow-parallel-fanout-design.md
git commit -m "test: add fan-out configuration measurement matrix and record results"
```

---

### Task 7: Update the agent guide and walker documentation

**Files:**
- Modify: `AGENTS.md`
- Modify: `ScannerCore/DirectoryWalkEngine.cs:9-14`

**Interfaces:**
- Consumes: final defaults from Task 6.

- [ ] **Step 1: Update `AGENTS.md`**

Replace the parallelism bullet under "Windows / scanning specifics":

```markdown
- **Parallelism**: directories above `ScanTreeBudget.ParallelFanOutLevels` (default 2 — the root and its immediate children) fan their children out across a scan-wide slot budget (`MaxDegreeOfParallelism`, default `Min(ProcessorCount, 16)`); deeper subtrees walk sequentially on one slot each. Fan-out only happens when `VolumeParallelismPolicy` detects no seek penalty (SSD/NVMe); HDDs and unknown volumes stay sequential.
```

- [ ] **Step 2: Update the engine doc comment**

In `ScannerCore/DirectoryWalkEngine.cs`, replace "Top-level subtrees walk in parallel on SSD-class volumes only" with:

```csharp
/// (NtQueryDirectoryFile) and delegates the actual walk to the bounded, budget-aware
/// <see cref="BoundedDirectoryWalker"/>. Always available. On SSD-class volumes the walk
/// fans out for the first <see cref="ScanTreeBudget.ParallelFanOutLevels"/> levels under a
/// shared slot budget; spinning disks stay fully sequential.
```

- [ ] **Step 3: Verify the build and full suite**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add AGENTS.md ScannerCore/DirectoryWalkEngine.cs
git commit -m "docs: describe depth-limited fan-out and shared slot budget"
```

---

## Verification checklist

Run before considering the plan complete:

```powershell
dotnet build SizeScanner.slnx -c Release
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release
dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release
dotnet publish .\SizeScanner.Avalonia\SizeScanner.Avalonia.csproj -c Release -r win-x64
```

The publish step matters: the walker is on the native AOT path, and the async rewrite must not introduce a trimming warning.
