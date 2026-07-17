# Bounded FsItem Scanning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep exact scan sizes while bounding the retained `FsItem` tree by aggregating less significant children into a typed chart `[Other]` entry during scanning.

**Architecture:** `DirectoryScanner` exposes native entries as temporary spans instead of materializing a `List<FsItem>`. `DirectoryWalkEngine` keeps a fixed top-K hierarchy under a global node/depth budget, accumulates discarded sizes into aggregate `FsItem` nodes, and returns the existing tree contract; the chart merges scanner aggregates with its own layout overflow into one `ChartDisplayMetadata.OtherName` segment.

**Tech Stack:** .NET 10, C# 14, Windows `NtQueryDirectoryFile`, Avalonia 11, CommunityToolkit.Mvvm, xUnit v3, BCL collections/concurrency only.

## Global Constraints

- Keep filesystem and native logic in `ScannerCore/`; `ScannerCore` must not reference `SizeScanner.Avalonia`.
- Preserve drive allocation-size and directory logical-size semantics.
- Preserve reparse handling: skip reparse points unless `FILE_ATTRIBUTE_OFFLINE`.
- Preserve `Items == null` exclusively for inaccessible directories.
- Default limits: `100_000` retained real nodes, `99` retained children per directory, depth `6`, and `10_000` inaccessible-path samples.
- Drive `[Free space]` and `[Inaccessible]` entries may add exactly two nodes beyond the real-node budget.
- A scanner aggregate has `FsItemKind.Aggregate`, an empty core name, exact size, no path, and no children.
- Only the chart layer maps an aggregate to `ChartDisplayMetadata.OtherName`.
- A directory with discarded descendants sets `HasUnretainedChildren = true`, even when its allowance cannot hold an aggregate child.
- Completed scans retain exact reachable byte totals; inaccessible content remains represented by existing drive occupied-minus-scanned semantics.
- An unexpected `NtQueryDirectoryFile` failure aborts the engine instead of publishing partial totals; inability to open one directory remains a bounded inaccessible entry.
- Cancellation throws and does not publish a partial scan.
- Scope navigation keeps the original root plus one completed scope tree; while replacing a visible scope, one additional bounded tree may exist transiently until the scan completes.
- Keep native AOT and trimming compatibility and add no NuGet dependency.

---

## File Structure

### New production files

- `ScannerCore/ScanTreeBudget.cs` — validated retained-node, fan-out, depth, diagnostics, and worker limits.
- `ScannerCore/DirectoryEntryCursor.cs` — internal native cursor/source/sink contracts.
- `ScannerCore/BoundedChildCollector.cs` — top-K selection and exact hidden-size aggregation.
- `ScannerCore/BoundedDirectoryWalker.cs` — one-pass bounded post-order traversal.

### New test files

- `ScannerCore.Tests/ScanTreeBudgetTests.cs`
- `ScannerCore.Tests/DirectoryEntryCursorTests.cs`
- `ScannerCore.Tests/BoundedChildCollectorTests.cs`
- `ScannerCore.Tests/BoundedDirectoryWalkerTests.cs`
- `ScannerCore.Tests/SyntheticDirectoryEntrySource.cs`
- `ScannerCore.Tests/BoundedScanMemoryTests.cs`

### Existing files modified

- `ScannerCore/FsItem.cs`
- `ScannerCore/DirectoryScanner.cs`
- `ScannerCore/DirectoryWalkEngine.cs`
- `ScannerCore/IScanEngine.cs`
- `ScannerCore/ScanEngineSelector.cs`
- `ScannerCore/DriveScanner.cs`
- `ScannerCore/DriveScanMetadata.cs`
- `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`
- `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`
- `SizeScanner.Avalonia/Charting/SunburstSegment.cs`
- `SizeScanner.Avalonia/Abstractions/IScanService.cs`
- `SizeScanner.Avalonia/Services/ScanService.cs`
- `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`
- `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`
- `SizeScanner.Avalonia/Views/ChartView.axaml.cs`
- `ScannerConsole/Program.cs`
- Relevant ScannerCore and Avalonia tests.

---

### Task 1: Add bounded-tree semantics to `FsItem`

**Files:**
- Create: `ScannerCore/ScanTreeBudget.cs`
- Modify: `ScannerCore/FsItem.cs:10-59`
- Create: `ScannerCore.Tests/ScanTreeBudgetTests.cs`
- Modify: `ScannerCore.Tests/FsItemTests.cs`

**Interfaces:**
- Produces: `FsItemKind`, `FsItem.CreateAggregate`, `HasUnretainedChildren`, `CountRetainedNodes`, `ScanTreeBudget`.
- Preserves: `FsItem(string, long, bool)`, `IsDir`, `Parent`, `Items`, and `TryGetPathFrom`.

- [ ] **Step 1: Write failing model tests**

```csharp
[Fact]
public void Aggregate_is_non_directory_non_actionable_core_node()
{
    var aggregate = FsItem.CreateAggregate(1_024);

    Assert.Equal(FsItemKind.Aggregate, aggregate.Kind);
    Assert.Equal(string.Empty, aggregate.Name);
    Assert.Equal(1_024, aggregate.Size);
    Assert.False(aggregate.IsDir);
    Assert.Null(aggregate.Items);
}

[Fact]
public void CountRetainedNodes_counts_only_the_bounded_object_graph()
{
    var root = new FsItem("root", 10, isDir: true);
    root.AttachChildren(
    [
        new FsItem("kept.bin", 5, isDir: false),
        FsItem.CreateAggregate(5)
    ]);

    Assert.Equal(3, root.CountRetainedNodes());
}

[Fact]
public void Budget_rejects_values_that_cannot_hold_root_and_aggregate()
{
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        new ScanTreeBudget(maxRetainedNodes: 1));
}
```

- [ ] **Step 2: Run the focused tests and verify failure**

```powershell
rtk dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~FsItemTests|FullyQualifiedName~ScanTreeBudgetTests"
```

Expected: compilation fails because the new model members do not exist.

- [ ] **Step 3: Add the validated budget**

```csharp
namespace ScannerCore;

public sealed record ScanTreeBudget
{
    public static ScanTreeBudget Default { get; } = new();

    public ScanTreeBudget(
        int maxRetainedNodes = 100_000,
        int maxChildrenPerDirectory = 99,
        int maxRetainedDepth = 6,
        int maxInaccessiblePaths = 10_000,
        int maxDegreeOfParallelism = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedNodes, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChildrenPerDirectory, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedDepth, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxInaccessiblePaths);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);

        MaxRetainedNodes = maxRetainedNodes;
        MaxChildrenPerDirectory = maxChildrenPerDirectory;
        MaxRetainedDepth = maxRetainedDepth;
        MaxInaccessiblePaths = maxInaccessiblePaths;
        MaxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    public int MaxRetainedNodes { get; }
    public int MaxChildrenPerDirectory { get; }
    public int MaxRetainedDepth { get; }
    public int MaxInaccessiblePaths { get; }
    public int MaxDegreeOfParallelism { get; }
}
```

- [ ] **Step 4: Extend `FsItem` without breaking callers**

```csharp
public enum FsItemKind
{
    File,
    Directory,
    Aggregate
}

public sealed class FsItem
{
    public FsItem(string name, long size, bool isDir)
        : this(name, size, isDir ? FsItemKind.Directory : FsItemKind.File)
    {
    }

    private FsItem(string name, long size, FsItemKind kind)
    {
        Name = name;
        Size = size;
        Kind = kind;
    }

    public string Name { get; }
    public long Size { get; set; }
    public FsItemKind Kind { get; }
    public bool IsDir => Kind == FsItemKind.Directory;
    public bool IsAggregate => Kind == FsItemKind.Aggregate;
    public bool HasUnretainedChildren { get; internal set; }
    public FsItem? Parent { get; internal set; }
    public List<FsItem>? Items { get; set; }

    public static FsItem CreateAggregate(long size) =>
        new(string.Empty, size, FsItemKind.Aggregate);

    internal void AttachChildren(List<FsItem> children)
    {
        Items = children;
        foreach (var child in children)
            child.Parent = this;
    }

    public int CountRetainedNodes()
    {
        var count = 1;
        if (Items is null)
            return count;
        foreach (var child in Items)
            count = checked(count + child.CountRetainedNodes());
        return count;
    }
}
```

Retain the existing `TryGetPathFrom` implementation unchanged.

- [ ] **Step 5: Run tests and commit**

```powershell
rtk dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~FsItemTests|FullyQualifiedName~ScanTreeBudgetTests"
rtk git add ScannerCore/ScanTreeBudget.cs ScannerCore/FsItem.cs ScannerCore.Tests/ScanTreeBudgetTests.cs ScannerCore.Tests/FsItemTests.cs && rtk git commit -m "feat: add bounded FsItem semantics"
```

---

### Task 2: Stream native directory batches

**Files:**
- Create: `ScannerCore/DirectoryEntryCursor.cs`
- Modify: `ScannerCore/DirectoryScanner.cs:74-176`
- Create: `ScannerCore.Tests/DirectoryEntryCursorTests.cs`
- Modify: `ScannerCore.Tests/DirectoryScannerParsingTests.cs`

**Interfaces:**
- Produces: `IDirectoryEntrySource.Open`, `IDirectoryEntryCursor.ReadNext`, `IDirectoryEntrySink.OnEntry`.
- Keeps temporarily: `DirectoryScanner.Scan(string, ref long)` as an adapter until Task 8.

- [ ] **Step 1: Write a failing cursor test**

```csharp
[Fact]
public void Cursor_reports_entries_batch_by_batch()
{
    using var temp = new TemporaryDirectory();
    temp.CreateFile("alpha.txt", 100);
    temp.CreateFile("beta.bin", 250);

    var scanner = new DirectoryScanner(preferAllocatedSize: false);
    using var cursor = scanner.Open(
        temp.Path + Path.DirectorySeparatorChar);
    Assert.NotNull(cursor);

    var sink = new RecordingSink();
    var buffer = new byte[DirectoryScanner.BufferSize];
    while (cursor!.ReadNext(buffer, sink) == DirectoryBatchResult.Entries)
    {
    }

    Assert.Equal(350, sink.Size);
    Assert.Equal(["alpha.txt", "beta.bin"], sink.Names.Order());
}

private sealed class RecordingSink : IDirectoryEntrySink
{
    public List<string> Names { get; } = [];
    public long Size { get; private set; }

    public void OnEntry(
        ReadOnlySpan<char> name,
        long size,
        bool isDirectory)
    {
        Names.Add(name.ToString());
        Size += size;
    }
}
```

- [ ] **Step 2: Run the test and verify failure**

```powershell
rtk dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~DirectoryEntryCursorTests"
```

- [ ] **Step 3: Add cursor contracts**

```csharp
namespace ScannerCore;

internal enum DirectoryBatchResult
{
    Entries,
    Completed,
    Failed
}

internal interface IDirectoryEntrySink
{
    void OnEntry(ReadOnlySpan<char> name, long size, bool isDirectory);
}

internal interface IDirectoryEntryCursor : IDisposable
{
    DirectoryBatchResult ReadNext(
        Span<byte> buffer,
        IDirectoryEntrySink sink);
}

internal interface IDirectoryEntrySource
{
    IDirectoryEntryCursor? Open(string path);
}
```

- [ ] **Step 4: Refactor `DirectoryScanner` into a source**

Keep all P/Invoke and record offsets in `DirectoryScanner.cs`. Expose one native query per cursor call:

```csharp
internal const int BufferSize = 1024 * 1024;

internal IDirectoryEntryCursor? Open(string path)
{
    var handle = NativeMethods.CreateFile(
        path,
        FileListDirectory,
        FileShare.ReadWrite | FileShare.Delete,
        IntPtr.Zero,
        FileMode.Open,
        FileFlagBackupSemantics,
        IntPtr.Zero);

    return handle.IsInvalid
        ? null
        : new Cursor(handle, PreferAllocatedSize);
}
```

The parser must pass a temporary span and never call `new string`:

```csharp
var nameLengthBytes = Unsafe.ReadUnaligned<uint>(
    ptr + OffsetFileNameLength);
var name = new ReadOnlySpan<char>(
    (char*)(ptr + OffsetFileName),
    checked((int)(nameLengthBytes / 2)));
var isDirectory = (attributes & FileAttributeDirectory) != 0;

if (!(isDirectory
    && (name.SequenceEqual(".".AsSpan())
        || name.SequenceEqual("..".AsSpan()))))
{
    var size = preferAllocatedSize
        ? Unsafe.ReadUnaligned<long>(ptr + OffsetAllocationSize)
        : Unsafe.ReadUnaligned<long>(ptr + OffsetEndOfFile);
    sink.OnEntry(name, size, isDirectory);
}
```

Preserve reparse/offline filtering before invoking the sink.

- [ ] **Step 5: Keep the old materializer as a cursor adapter**

Implement `DirectoryScanner.Scan` through a temporary `LegacyFsItemSink`. This keeps the branch buildable while the bounded walker is developed; remove both in Task 8.

- [ ] **Step 6: Run parsing tests and commit**

```powershell
rtk dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~DirectoryScannerParsingTests|FullyQualifiedName~DirectoryEntryCursorTests"
rtk git add ScannerCore/DirectoryEntryCursor.cs ScannerCore/DirectoryScanner.cs ScannerCore.Tests/DirectoryEntryCursorTests.cs ScannerCore.Tests/DirectoryScannerParsingTests.cs && rtk git commit -m "refactor: stream native directory entries"
```

---

### Task 3: Select top-K children and aggregate the remainder

**Files:**
- Create: `ScannerCore/BoundedChildCollector.cs`
- Create: `ScannerCore.Tests/BoundedChildCollectorTests.cs`

**Interfaces:**
- Produces: `ConsiderFile`, `ConsiderDirectory`, `BuildChildren`, `HasHiddenChildren`.
- Consumes: bounded `FsItem` model from Task 1.

- [ ] **Step 1: Write failing collector tests**

```csharp
[Fact]
public void Collector_keeps_largest_children_and_sums_the_rest()
{
    var collector = new BoundedChildCollector(maxChildren: 2);
    collector.ConsiderDirectory(new FsItem("small", 10, true));
    collector.ConsiderDirectory(new FsItem("large", 30, true));
    collector.ConsiderDirectory(new FsItem("medium", 20, true));

    var children = collector.BuildChildren();

    Assert.Equal(["large", "medium"], children.Take(2).Select(x => x.Name));
    Assert.True(children[^1].IsAggregate);
    Assert.Equal(10, children[^1].Size);
    Assert.True(collector.HasHiddenChildren);
}

[Fact]
public void Collector_uses_ordinal_name_as_stable_size_tie_breaker()
{
    var collector = new BoundedChildCollector(maxChildren: 1);
    collector.ConsiderDirectory(new FsItem("z", 10, true));
    collector.ConsiderDirectory(new FsItem("a", 10, true));

    Assert.Equal("a", collector.BuildChildren()[0].Name);
}
```

- [ ] **Step 2: Run tests and verify failure**

```powershell
rtk dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~BoundedChildCollectorTests"
```

- [ ] **Step 3: Implement lazy file admission**

`ConsiderFile` must compare the span against the worst retained item before allocating a managed name:

```csharp
public void ConsiderFile(ReadOnlySpan<char> name, long size)
{
    if (!WouldRetain(size, name))
    {
        Hide(size);
        return;
    }

    var ownedName = name.ToString();
    Consider(new FsItem(ownedName, size, isDir: false));
}

private bool WouldRetain(long size, ReadOnlySpan<char> name)
{
    if (_maxChildren == 0)
        return false;
    if (_kept.Count < _maxChildren)
        return true;

    _kept.TryPeek(out var worst, out _);
    return size > worst!.Size
        || size == worst.Size
        && name.CompareTo(
            worst.Name.AsSpan(), StringComparison.Ordinal) < 0;
}
```

- [ ] **Step 4: Implement directory admission and aggregate output**

```csharp
public void ConsiderDirectory(FsItem directory) => Consider(directory);

private void Consider(FsItem candidate)
{
    _kept.Enqueue(candidate, ItemPriority.For(candidate));
    if (_kept.Count > _maxChildren)
        Hide(_kept.Dequeue().Size);
}

private void Hide(long size)
{
    _hiddenSize = checked(_hiddenSize + size);
    _hiddenCount = checked(_hiddenCount + 1);
}

public List<FsItem> BuildChildren()
{
    var children = _kept.UnorderedItems
        .Select(item => item.Element)
        .OrderByDescending(item => item.Size)
        .ThenBy(item => item.Name, StringComparer.Ordinal)
        .ToList();

    if (_hiddenCount > 0)
        children.Add(FsItem.CreateAggregate(_hiddenSize));
    return children;
}

public bool HasHiddenChildren => _hiddenCount > 0;

private readonly record struct ItemPriority(long Size, string Name)
    : IComparable<ItemPriority>
{
    public static ItemPriority For(FsItem item) =>
        new(item.Size, item.Name);

    public int CompareTo(ItemPriority other)
    {
        var bySize = Size.CompareTo(other.Size);
        return bySize != 0
            ? bySize
            : -StringComparer.Ordinal.Compare(Name, other.Name);
    }
}
```

The reversed ordinal comparison makes the queue head the worst retained item for equal sizes.

- [ ] **Step 5: Run tests and commit**

```powershell
rtk dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~BoundedChildCollectorTests"
rtk git add ScannerCore/BoundedChildCollector.cs ScannerCore.Tests/BoundedChildCollectorTests.cs && rtk git commit -m "feat: aggregate insignificant scan children"
```

---

### Task 4: Build the bounded `FsItem` walker

**Files:**
- Create: `ScannerCore/BoundedDirectoryWalker.cs`
- Create: `ScannerCore.Tests/SyntheticDirectoryEntrySource.cs`
- Create: `ScannerCore.Tests/BoundedDirectoryWalkerTests.cs`

**Interfaces:**
- Produces: `BoundedDirectoryWalker.Scan(...) -> ScanResult`.
- Consumes: `IDirectoryEntrySource`, `ScanTreeBudget`, `BoundedChildCollector`.

- [ ] **Step 1: Add an on-demand synthetic source**

Implement a fake cursor that generates file names into a stack span from counters. Its wide-directory factory must not allocate an object graph proportional to `entryCount`:

```csharp
internal static IDirectoryEntrySource WideDirectory(
    string root,
    int entryCount,
    long size,
    bool directories = false) =>
    new SyntheticDirectoryEntrySource(
        root, entryCount, size, directories);
```

The fake returns `Missing` for generated child paths when `directories` is true, allowing inaccessible-path cap tests without creating directories on disk.

Implement `SyntheticDirectoryEntrySource` as one cursor whose names are
generated into the supplied span:

```csharp
internal sealed class SyntheticDirectoryEntrySource(
    string root,
    int entryCount,
    long size,
    bool directories) : IDirectoryEntrySource
{
    public IDirectoryEntryCursor? Open(string path) =>
        Normalize(path).Equals(
            Normalize(root), StringComparison.OrdinalIgnoreCase)
            ? new Cursor(entryCount, size, directories)
            : null;

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar);

private sealed class Cursor(
    int entryCount,
    long size,
    bool directories) : IDirectoryEntryCursor
{
    private int _index;

    public DirectoryBatchResult ReadNext(
        Span<byte> buffer,
        IDirectoryEntrySink sink)
    {
        if (_index >= entryCount)
            return DirectoryBatchResult.Completed;

        var end = Math.Min(entryCount, _index + 256);
        for (; _index < end; _index++)
        {
            Span<char> name = stackalloc char[32];
            "entry-".AsSpan().CopyTo(name);
            _index.TryFormat(
                name[6..], out var written, "D8");
            sink.OnEntry(
                name[..(6 + written)], size, directories);
        }

        return DirectoryBatchResult.Entries;
    }

    public void Dispose() { }
}
}
```

Normalize trailing separators before comparing the requested path with the
synthetic root.

- [ ] **Step 2: Write hard-bound tests**

```csharp
[Fact]
public void Million_file_directory_keeps_exact_size_with_bounded_tree()
{
    var source = SyntheticSources.WideDirectory(
        @"C:\wide", 1_000_000, size: 4);
    var budget = new ScanTreeBudget(
        maxRetainedNodes: 103,
        maxChildrenPerDirectory: 99,
        maxRetainedDepth: 6);

    var result = new BoundedDirectoryWalker(source).Scan(
        @"C:\wide", false, budget,
        CancellationToken.None, null);

    Assert.Equal(4_000_000, result.Total);
    Assert.Equal(4_000_000, result.Root.Size);
    Assert.True(result.Root.CountRetainedNodes() <= 103);
    Assert.Contains(result.Root.Items!, item => item.IsAggregate);
}

[Fact]
public void Depth_limit_keeps_size_and_marks_directory_as_truncated()
{
    using var temp = new TemporaryDirectory();
    var relative = string.Join(
        Path.DirectorySeparatorChar,
        Enumerable.Range(0, 20).Select(index => $"d{index}"));
    temp.CreateFile(Path.Combine(relative, "leaf.bin"), 7);
    var source = new DirectoryScanner(preferAllocatedSize: false);
    var budget = new ScanTreeBudget(
        maxRetainedNodes: 100,
        maxRetainedDepth: 3);

    var result = new BoundedDirectoryWalker(source).Scan(
        temp.Path, false, budget,
        CancellationToken.None, null);

    Assert.Equal(7, result.Total);
    Assert.True(MaxDepth(result.Root) <= 4);
    Assert.True(DeepestDirectory(result.Root).HasUnretainedChildren);
}

private static int MaxDepth(FsItem item) =>
    item.Items is not { Count: > 0 }
        ? 1
        : 1 + item.Items.Max(MaxDepth);

private static FsItem DeepestDirectory(FsItem item)
{
    var current = item;
    while (current.Items?.FirstOrDefault(
        child => child.IsDir) is { } child)
        current = child;
    return current;
}
```

- [ ] **Step 3: Partition node allowance**

Reserve one node for the current directory, one possible aggregate, and one scratch child:

```csharp
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

private readonly record struct ChildAllowance(
    int Slots,
    int NodesPerChild);
```

The retained output invariant is:

```text
directory + retained children + optional aggregate <= allowance
```

- [ ] **Step 4: Implement post-order scanning**

```csharp
internal ScanResult Scan(
    string target,
    bool isDriveScan,
    ScanTreeBudget budget,
    CancellationToken token,
    Action<string, long>? onProgress)
{
    var context = new WalkContext(
        _source, budget, token, onProgress);
    var buffer = ArrayPool<byte>.Shared.Rent(
        DirectoryScanner.BufferSize);
    try
    {
        var root = WalkDirectory(
            target, target, 0, budget.MaxRetainedNodes,
            buffer, context);
        return new ScanResult
        {
            Root = root,
            Total = context.Total,
            Inaccessible = context.Inaccessible,
            InaccessibleCount = context.InaccessibleCount,
            InaccessiblePathsTruncated =
                context.InaccessiblePathsTruncated
        };
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}

private FsItem WalkDirectory(
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

    var partition = depth + 1 >= context.Budget.MaxRetainedDepth
        ? new ChildAllowance(0, 0)
        : DivideAllowance(
            allowance, context.Budget.MaxChildrenPerDirectory);
    var collector = new BoundedChildCollector(partition.Slots);
    long total = 0;

    while (true)
    {
        context.Token.ThrowIfCancellationRequested();
        var sink = new BatchSink(collector);
        var status = cursor.ReadNext(buffer, sink);
        total = checked(total + sink.FileSize);
        context.AddToTotal(sink.FileSize);

        foreach (var childName in sink.Directories)
        {
            var child = WalkDirectory(
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

    var selected = collector.BuildChildren();
    item.HasUnretainedChildren = collector.HasHiddenChildren;
    if (allowance <= 1 && item.HasUnretainedChildren)
        selected.Clear();
    item.AttachChildren(selected);
    item.Size = total;
    return item;
}
```

Add the per-batch sink:

```csharp
private sealed class BatchSink(
    BoundedChildCollector collector) : IDirectoryEntrySink
{
    public List<string> Directories { get; } = [];
    public long FileSize { get; private set; }

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
```

- [ ] **Step 5: Cap inaccessible paths**

`WalkContext` keeps an exact `InaccessibleCount`, stores at most `MaxInaccessiblePaths`, and exposes `InaccessiblePathsTruncated`. Add these fields to `ScanResult` while preserving its existing `Inaccessible` property:

```csharp
public sealed class ScanResult
{
    public required FsItem Root { get; init; }
    public required long Total { get; init; }
    public required IReadOnlyList<string> Inaccessible { get; init; }
    public long InaccessibleCount { get; init; }
    public bool InaccessiblePathsTruncated { get; init; }
}
```

Implement the sequential context as:

```csharp
private sealed class WalkContext(
    IDirectoryEntrySource source,
    ScanTreeBudget budget,
    CancellationToken token,
    Action<string, long>? onProgress)
{
    private readonly List<string> _inaccessible = [];
    private long _total;

    public IDirectoryEntrySource Source { get; } = source;
    public ScanTreeBudget Budget { get; } = budget;
    public CancellationToken Token { get; } = token;
    public long Total => _total;
    public IReadOnlyList<string> Inaccessible => _inaccessible;
    public long InaccessibleCount { get; private set; }
    public bool InaccessiblePathsTruncated =>
        InaccessibleCount > _inaccessible.Count;

    public void AddToTotal(long size) =>
        _total = checked(_total + size);

    public void AddInaccessible(string path)
    {
        InaccessibleCount = checked(InaccessibleCount + 1);
        if (_inaccessible.Count < budget.MaxInaccessiblePaths)
            _inaccessible.Add(path);
    }

    public void Report(string path) =>
        onProgress?.Invoke(path, _total);
}
```

- [ ] **Step 6: Run bounded walker tests and commit**

```powershell
rtk dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release --filter "FullyQualifiedName~BoundedDirectoryWalkerTests"
rtk git add ScannerCore/BoundedDirectoryWalker.cs ScannerCore/IScanEngine.cs ScannerCore.Tests/SyntheticDirectoryEntrySource.cs ScannerCore.Tests/BoundedDirectoryWalkerTests.cs && rtk git commit -m "feat: build bounded FsItem trees"
```

---

### Task 5: Cut the scan engine over and preserve bounded SSD parallelism

**Files:**
- Modify: `ScannerCore/IScanEngine.cs`
- Modify: `ScannerCore/ScanEngineSelector.cs`
- Replace implementation: `ScannerCore/DirectoryWalkEngine.cs`
- Modify: `ScannerCore/DriveScanner.cs`
- Modify: `ScannerCore.Tests/DirectoryWalkEngineTests.cs`
- Modify: `ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs`
- Modify: `ScannerCore.Tests/DriveScannerTests.cs`
- Modify: `ScannerCore.Tests/ScanEngineSelectorTests.cs`

**Interfaces:**
- Final engine signature adds `ScanTreeBudget` as the last parameter.
- Existing `DriveScanner` call sites remain source-compatible through an optional last parameter.

- [ ] **Step 1: Change engine tests first**

```csharp
ScanResult Scan(
    string target,
    bool isDriveScan,
    CancellationToken token,
    Action<string, long>? onProgress,
    ScanTreeBudget budget);
```

Update fake engines and assert that the selector forwards the same budget to the selected/fallback engine.

- [ ] **Step 2: Delegate `DirectoryWalkEngine` to the bounded walker**

```csharp
public ScanResult Scan(
    string target,
    bool isDriveScan,
    CancellationToken token,
    Action<string, long>? onProgress,
    ScanTreeBudget budget)
{
    var source = new DirectoryScanner(
        preferAllocatedSize: isDriveScan);
    return new BoundedDirectoryWalker(
        source,
        parallelizeTopLevel: _shouldParallelize(target))
        .Scan(target, isDriveScan, budget, token, onProgress);
}
```

- [ ] **Step 3: Keep public facade calls compatible**

```csharp
public FsItem ScanDrive(
    string driveName,
    CancellationToken cancellationToken = default,
    IProgress<ScanProgress>? progress = null,
    ScanTreeBudget? budget = null);

public FsItem ScanDirectory(
    string path,
    CancellationToken cancellationToken,
    IProgress<ScanProgress>? progress = null,
    ScanTreeBudget? budget = null);
```

Pass `budget ?? ScanTreeBudget.Default` to the engine. Expose `InaccessibleCount` and `InaccessiblePathsTruncated` on `DriveScanner`.

- [ ] **Step 4: Implement bounded top-level work queues**

For SSD-class targets only, enumerate root directories into a bounded `Channel<DirectoryWorkItem>` with capacity `2 * degree`. Each worker:

1. rents one native buffer;
2. scans one subtree sequentially with `NodesPerChild` allowance;
3. writes one bounded subtree to a bounded result channel;
4. releases the buffer when the worker completes.

A single result consumer feeds subtrees into the root `BoundedChildCollector`, so completion order cannot change output ordering. HDD and unknown volumes call the sequential walker directly.

```csharp
var work = Channel.CreateBounded<DirectoryWorkItem>(
    new BoundedChannelOptions(2 * degree)
    {
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });

var results = Channel.CreateBounded<FsItem>(
    new BoundedChannelOptions(2 * degree)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
```

Make inaccessible counters thread-safe with `Interlocked`; guard the sampled path list with a lock.

- [ ] **Step 5: Assert sequential/parallel equivalence and bounds**

```csharp
Assert.Equal(sequential.Total, parallel.Total);
Assert.Equal(
    sequential.Root.Items!.Select(x => (x.Name, x.Size)),
    parallel.Root.Items!.Select(x => (x.Name, x.Size)));
Assert.True(
    parallel.Root.CountRetainedNodes()
    <= budget.MaxRetainedNodes);
Assert.True(maxObservedQueueDepth <= 2 * degree);
```

- [ ] **Step 6: Run core tests and commit**

```powershell
rtk dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release
rtk git add ScannerCore/IScanEngine.cs ScannerCore/ScanEngineSelector.cs ScannerCore/DirectoryWalkEngine.cs ScannerCore/DriveScanner.cs ScannerCore/BoundedDirectoryWalker.cs ScannerCore.Tests/DirectoryWalkEngineTests.cs ScannerCore.Tests/DirectoryWalkEngineParallelTests.cs ScannerCore.Tests/DriveScannerTests.cs ScannerCore.Tests/ScanEngineSelectorTests.cs && rtk git commit -m "feat: use bounded scan engine"
```

---

### Task 6: Merge scanner and layout overflow into one chart `[Other]`

**Files:**
- Modify: `SizeScanner.Avalonia/Charting/ChartNodeRules.cs`
- Modify: `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs:30-380`
- Modify: `SizeScanner.Avalonia/Charting/SunburstSegment.cs`
- Modify: `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`
- Modify: `SizeScanner.Avalonia.Tests/SunburstChartBuilderCapTests.cs`
- Modify: `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`

**Interfaces:**
- Produces: `ChartNodeRules.IsAggregate`, aggregate display-name mapping, and one `Other` segment per parent/ring.
- Consumes: `FsItemKind.Aggregate`.

- [ ] **Step 1: Write failing aggregate projection tests**

```csharp
[Fact]
public void Scanner_aggregate_renders_as_other_and_is_not_actionable()
{
    var root = TestTree.Dir("root",
        TestTree.File("kept", 80),
        FsItem.CreateAggregate(20));

    var chart = new SunburstChartBuilder().Build(root, 0);
    var other = Assert.Single(
        chart.Segments,
        segment => segment.DisplayName
            == ChartDisplayMetadata.OtherName);

    Assert.Equal(20, other.Size);
    Assert.False(other.IsActionable);
}

[Fact]
public void Scanner_aggregate_and_layout_overflow_merge_into_one_other()
{
    var children = Enumerable.Range(0, 150)
        .Select(index => TestTree.File($"f{index}", 1))
        .Append(FsItem.CreateAggregate(50))
        .ToArray();
    var root = TestTree.Dir("root", children);

    var chart = new SunburstChartBuilder().Build(root, 0);

    Assert.Single(
        chart.Segments,
        segment => segment.DisplayName
            == ChartDisplayMetadata.OtherName);
}
```

- [ ] **Step 2: Make aggregate policy type-based**

```csharp
public static bool IsAggregate(FsItem? item) =>
    item?.Kind == FsItemKind.Aggregate;

public static bool IsSyntheticSegment(FsItem? item) =>
    item is null
    || IsAggregate(item)
    || item.Name is DriveScanMetadata.FreeSpaceName
        or DriveScanMetadata.InaccessibleName
        or ChartDisplayMetadata.FilteredName
        or ChartDisplayMetadata.OtherName;

public static bool IsScopable(FsItem? item) =>
    item is { IsDir: true, Items: not null }
    && (item.Items.Count > 0 || item.HasUnretainedChildren)
    && !IsSyntheticSegment(item);
```

- [ ] **Step 3: Add explicit segment display names**

```csharp
public sealed record SunburstSegment(
    FsItem? Node,
    string DisplayName,
    int Level,
    int RingIndex,
    long Size,
    double StartAngle,
    double SweepAngle,
    Color Color)
{
    public bool IsActionable =>
        Node is not null
        && !ChartNodeRules.IsSyntheticSegment(Node);

    public override string ToString() =>
        $"{DisplayName} Size: ({Humanize.Size(Size)})";
}
```

All ordinary segments use `Node.Name`; scanner and layout aggregates use `ChartDisplayMetadata.OtherName`.

- [ ] **Step 4: Merge aggregate accounting in `EmitRing`**

Do not add scanner aggregates to `candidates`. Record their displayed and real sizes in `ParentRingState`:

```csharp
if (ChartNodeRules.IsAggregate(child))
{
    state.PreAggregatedDisplayed += displayed;
    state.PreAggregatedSize += child.Size;
    continue;
}
```

Only non-aggregate candidates contribute to `TotalDisplayed` and `TotalSize`;
the pre-aggregated fields are added exactly once by `AddOtherSegments`.

`AddOtherSegments` computes:

```csharp
var otherDisplayed =
    state.PreAggregatedDisplayed
    + state.TotalDisplayed
    - state.EmittedDisplayed;
var otherSize =
    state.PreAggregatedSize
    + state.TotalSize
    - state.EmittedSize;
```

Remove the current `candidates.Count == 0` early return before
`AddOtherSegments`, and remove the `state.EmittedCount < 1` guard there. Emit
an `Other` segment even when no real child was emitted, provided
`otherDisplayed > 0` and segment budget remains. Treat scanner aggregates as
always visible so the filter does not relabel them as `[Filtered]`.

- [ ] **Step 5: Run chart tests and commit**

```powershell
rtk dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~SunburstChartBuilderTests|FullyQualifiedName~SunburstChartBuilderCapTests|FullyQualifiedName~ChartViewModelTests"
rtk git add SizeScanner.Avalonia/Charting/ChartNodeRules.cs SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs SizeScanner.Avalonia/Charting/SunburstSegment.cs SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs SizeScanner.Avalonia.Tests/SunburstChartBuilderCapTests.cs SizeScanner.Avalonia.Tests/ChartViewModelTests.cs && rtk git commit -m "feat: render scanner aggregates as other"
```

---

### Task 7: Rescan selected scopes while keeping root plus current scope

**Files:**
- Modify: `SizeScanner.Avalonia/Abstractions/IScanService.cs`
- Modify: `SizeScanner.Avalonia/Services/ScanService.cs`
- Modify: `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`
- Modify: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`
- Modify: `SizeScanner.Avalonia/Views/ChartView.axaml.cs`
- Modify: `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`
- Modify: `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`
- Modify: `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`

**Interfaces:**
- Produces: `IScanService.RunScopeAsync`, async scope/up/root navigation, scope cancellation, stale-root tracking.
- Memory invariant: at most root tree plus current scope tree are retained.

- [ ] **Step 1: Split root and scope scan service methods**

```csharp
public interface IScanService
{
    string LastTarget { get; }
    bool IsDriveScan { get; }
    DriveScanner Scanner { get; }

    Task<FsItem> RunAsync(
        string target,
        bool isDrive,
        CancellationToken cancellationToken,
        IProgress<ScanProgress> progress,
        ScanTreeBudget? budget = null);

    Task<FsItem> RunScopeAsync(
        string target,
        CancellationToken cancellationToken,
        IProgress<ScanProgress> progress,
        ScanTreeBudget? budget = null);
}
```

`RunScopeAsync` creates a fresh `DriveScanner` and calls `ScanDirectory`, but does not change `LastTarget`, `IsDriveScan`, or the root scanner used by the inaccessible pane.

- [ ] **Step 2: Write failing scope tests**

Use a fake service that records root and scope calls. Assert:

- clicking a retained directory calls `RunScopeAsync` with its full path;
- clicking aggregate/file/free-space/inaccessible does not scan;
- the old chart remains visible until scope scan completes;
- a second scope replaces the first scoped tree rather than retaining both;
- `Go to root` uses the cached root when it is not stale;
- `Go up` rescans the parent unless it is the cached root;
- cancellation leaves the current chart unchanged.

- [ ] **Step 3: Inject scan service into `ChartViewModel`**

```csharp
public ChartViewModel(
    IScanService scan,
    IFileSystemActions fileSystem,
    IDialogService dialogs)
{
    _scan = scan;
    _fileSystem = fileSystem;
    _dialogs = dialogs;
}
```

Store:

```csharp
private FsItem? _scanRoot;
private FsItem? _scopedRoot;
private string _rootPath = string.Empty;
private string _scopePath = string.Empty;
private bool _rootIsDrive;
private bool _rootIsStale;
private CancellationTokenSource? _scopeCts;
[ObservableProperty] private bool _isScopeScanning;
[ObservableProperty] private string _scopeStatusText = string.Empty;

public void CancelScopeScan() => _scopeCts?.Cancel();
```

- [ ] **Step 4: Implement async scope**

```csharp
public async Task<bool> TryScopeAtAsync(FsItem node)
{
    if (!CanScopeTo(node) || IsScopeScanning)
        return false;

    var path = BuildFullPath(AncestorChain(node));
    using var cts = new CancellationTokenSource();
    _scopeCts = cts;
    IsScopeScanning = true;
    try
    {
        var scanned = await _scan.RunScopeAsync(
            path,
            cts.Token,
            new Progress<ScanProgress>(
                progress => ScopeStatusText = progress.CurrentPath),
            ScanTreeBudget.Default);

        _scopedRoot = scanned;
        _scopePath = path;
        RebuildLayout();
        UpdateScopeState();
        return true;
    }
    catch (OperationCanceledException)
        when (cts.IsCancellationRequested)
    {
        return false;
    }
    catch (Exception ex)
    {
        await _dialogs.ShowInfoAsync(
            "Scope scan failed", ex.Message);
        return false;
    }
    finally
    {
        _scopeCts = null;
        IsScopeScanning = false;
        ScopeStatusText = string.Empty;
    }
}
```

Once `_scopedRoot` is replaced, the previous scope tree has no owner and becomes collectible.
When scoped, `UpdateScopeState` sets `_displayRootPath = _scopePath` directly;
it must not call `TryGetPathFrom(_scanRoot)` because the rescanned scope has an
independent root.

- [ ] **Step 5: Implement bounded up/root navigation**

`GoUpAsync` derives `Directory.GetParent(_scopePath)`. If the parent equals `_rootPath`, clear scope; otherwise call `RunScopeAsync(parent)`.

`GoToRootAsync`:

- clears scope immediately when `_rootIsStale == false`;
- reruns the original root scan when stale;
- raises `RootRescanned(FsItem root)` after a successful stale-root scan;
- then clears `_scopedRoot`, `_scopePath`, and the stale flag.

`MainWindowViewModel` subscribes to `RootRescanned`, replaces its `_scanRoot`,
and repopulates inaccessible diagnostics from the root `ScanService.Scanner`.

- [ ] **Step 6: Keep delete behavior correct**

For an unscoped tree, retain the existing in-memory removal and ancestor-size propagation.

For a scoped tree:

1. remove from the scoped tree and update scoped ancestors;
2. set `_rootIsStale = true`;
3. let `Go to root` refresh the root before displaying it.

Never retain deleted scope snapshots as history.

- [ ] **Step 7: Make pointer scope invocation async**

```csharp
private async void OnPointerPressed(
    object? sender,
    PointerPressedEventArgs e)
{
    if (Vm is null)
        return;

    var point = e.GetCurrentPoint(_chart);
    var node = HitTest(e.GetPosition(_chart));
    if (point.Properties.IsLeftButtonPressed && node is not null)
        await Vm.TryScopeAtAsync(node);
}
```

`MainWindowViewModel.CancelScanCommand` cancels its root CTS or calls `Chart.CancelScopeScan()`. Include scope scan state in the status bar.

- [ ] **Step 8: Run UI tests and commit**

```powershell
rtk dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release
rtk git add SizeScanner.Avalonia/Abstractions/IScanService.cs SizeScanner.Avalonia/Services/ScanService.cs SizeScanner.Avalonia/ViewModels/ChartViewModel.cs SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs SizeScanner.Avalonia/Views/ChartView.axaml.cs SizeScanner.Avalonia.Tests/ScanServiceTests.cs SizeScanner.Avalonia.Tests/ChartViewModelTests.cs SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs && rtk git commit -m "feat: rescan bounded chart scopes"
```

---

### Task 8: Remove legacy materialization and verify memory/AOT

**Files:**
- Modify: `ScannerCore/DirectoryScanner.cs`
- Modify: `ScannerCore.Tests/DirectoryScannerParsingTests.cs`
- Modify: `ScannerConsole/Program.cs`
- Create: `ScannerCore.Tests/BoundedScanMemoryTests.cs`
- Modify: `README.md`

**Interfaces:**
- Removes: `LegacyFsItemSink` and `DirectoryScanner.Scan(string, ref long)`.
- Verifies: bounded retained graph, capped diagnostics, complete tests, native AOT publish.

- [ ] **Step 1: Remove the full-list adapter**

Delete `LegacyFsItemSink`, `DirectoryScanner.Scan`, and old parser signatures
that accept `List<FsItem>`. Rewrite `DirectoryScannerParsingTests` to consume
the cursor; retain its name/size, dot-directory, missing-directory, and
concurrent-call assertions.

```powershell
rtk rg "LegacyFsItemSink|List<FsItem>\\? Scan\\(" --glob "*.cs" .
```

Expected: no match.

- [ ] **Step 2: Add deterministic scaling tests**

```csharp
[Fact]
public void Retained_node_count_is_constant_as_input_grows()
{
    var budget = new ScanTreeBudget(
        maxRetainedNodes: 1_000,
        maxChildrenPerDirectory: 99,
        maxRetainedDepth: 6);

    var small = ScanSyntheticFiles(1_000, budget);
    var large = ScanSyntheticFiles(1_000_000, budget);

    Assert.True(
        small.Root.CountRetainedNodes()
        <= budget.MaxRetainedNodes);
    Assert.True(
        large.Root.CountRetainedNodes()
        <= budget.MaxRetainedNodes);
    Assert.Equal(1_000_000, large.Total);
}

private static ScanResult ScanSyntheticFiles(
    int fileCount,
    ScanTreeBudget budget)
{
    var source = SyntheticSources.WideDirectory(
        @"C:\memory-test", fileCount, size: 1);
    return new BoundedDirectoryWalker(source).Scan(
        @"C:\memory-test", false, budget,
        CancellationToken.None, null);
}
```

Add a million-inaccessible-directory test:

```csharp
[Fact]
public void Inaccessible_samples_are_bounded()
{
    var source = SyntheticSources.WideDirectory(
        @"C:\denied", 1_000_000, size: 0,
        directories: true);
    var budget = new ScanTreeBudget(
        maxRetainedNodes: 1_000,
        maxInaccessiblePaths: 32);

    var result = new BoundedDirectoryWalker(source).Scan(
        @"C:\denied", false, budget,
        CancellationToken.None, null);

    Assert.Equal(1_000_000, result.InaccessibleCount);
    Assert.Equal(32, result.Inaccessible.Count);
    Assert.True(result.InaccessiblePathsTruncated);
}
```

- [ ] **Step 3: Add an opt-in ten-million-entry diagnostic**

Gate with `SIZESCANNER_RUN_PERF_TESTS=1` and `[Trait("Category", "Performance")]`. Report elapsed time, `GC.GetTotalAllocatedBytes`, heap size, retained node count, and exact total. Assert only deterministic totals and retained-node bounds.

- [ ] **Step 4: Update console diagnostics**

Print:

```csharp
AnsiConsole.MarkupLine(
    $"[bold]Retained nodes:[/] {root.CountRetainedNodes():N0}");
AnsiConsole.MarkupLine(
    $"[bold]Inaccessible:[/] {scanner.Inaccessible.Length:N0} shown of {scanner.InaccessibleCount:N0}");
```

- [ ] **Step 5: Document behavior**

Add:

```markdown
### Memory-bounded scans

SizeScanner calculates exact reachable sizes but retains only a bounded set of
the largest entries. Less significant entries are combined into `[Other]`.
Opening a retained directory rescans that scope for more detail. `[Other]`
cannot be opened because it can represent multiple paths.
```

- [ ] **Step 6: Run final verification**

```powershell
rtk dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release
rtk dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release
rtk dotnet build SizeScanner.slnx -c Release
rtk dotnet publish SizeScanner.Avalonia/SizeScanner.Avalonia.csproj -c Release -r win-x64
```

Expected: tests, Release build, and native AOT/trimming publish pass.

- [ ] **Step 7: Commit**

```powershell
rtk git add ScannerCore/DirectoryScanner.cs ScannerCore.Tests/DirectoryScannerParsingTests.cs ScannerConsole/Program.cs ScannerCore.Tests/BoundedScanMemoryTests.cs README.md && rtk git commit -m "test: verify bounded scan memory"
```

---

## Acceptance Checklist

- [ ] Full scan size remains exact for every reachable entry.
- [ ] One million synthetic files retain no more than `MaxRetainedNodes`.
- [ ] Discarded file names are not allocated as managed strings.
- [ ] Wide directories do not create an unbounded `List<FsItem>`.
- [ ] Scanner and layout overflow produce one chart `[Other]` segment per parent.
- [ ] Aggregate and filtered segments remain non-actionable.
- [ ] `Items == null` continues to mean access denied only.
- [ ] Retained directories with hidden descendants remain scopable.
- [ ] Root plus current scope are the only long-lived scan trees.
- [ ] SSD queues and inaccessible path samples are bounded.
- [ ] Existing filter/free-space/hover/delete behavior remains covered.
- [ ] Release build and native AOT publish succeed.
