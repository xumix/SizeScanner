# Unified Scan Progress and Chart Spinner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show unified toolbar progress and cancellation UI for root and chart-scoped scans, with an indeterminate mode for directory scans and a circular chart overlay spinner during every scan.

**Architecture:** Keep root scan state in `MainWindowViewModel` and scoped scan state in `ChartViewModel`. Add observable scoped progress plus a derived all-scan chart busy state, then let the main view-model expose computed display progress selected from the active owner. Add a dependency-free Avalonia spinner control and place it in a pointer-blocking overlay above the existing chart.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1.1 compiled AXAML, CommunityToolkit.Mvvm 8.4.2, xUnit v3.

## Global Constraints

- Target Windows 10+ x64 with `net10.0-windows` and `win-x64`.
- Preserve Native AOT, trimming, compiled binding, and explicit-using compatibility.
- Do not add a spinner package; use Avalonia drawing and dispatcher primitives already referenced by the application.
- Preserve the bounded root-plus-current-scope snapshot and shared `ScanService` concurrency guards.
- Preserve cancellation as control flow and never replace the chart with a partial result.
- New C# files require the repository copyright/SPDX header, nullable-safe code, four-space indentation, and file-scoped namespaces.
- Run every shell command with the required `rtk` prefix.
- The user authorized focused task commits on `feat-scan-progress-spinner`; do not push, and never stage unrelated files from another worktree.

## File Structure

- Create `SizeScanner.Avalonia/Views/BusySpinnerControl.cs`: dependency-free circular busy indicator; owns only animation timing and rendering.
- Modify `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`: observable root/scoped busy state, scoped progress value/mode, progress cleanup.
- Modify `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`: root indeterminate state, unified display progress, robust root cleanup, chart-property propagation.
- Modify `SizeScanner.Avalonia/Views/MainWindow.axaml`: bind toolbar progress and cancellation controls to unified scan state.
- Modify `SizeScanner.Avalonia/Views/ChartView.axaml`: layer the blocking spinner overlay over the chart.
- Modify `SizeScanner.Avalonia.Tests/FakeScanService.cs`: retain progress reporters so tests can emit deterministic reports.
- Modify `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`: scoped progress, derived busy state, and cleanup coverage.
- Modify `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`: unified progress and root cleanup coverage.
- Modify `SizeScanner.Avalonia.Tests/ViewLocatorTests.cs`: assert the chart view contains the app-owned spinner.
- Modify `AGENTS.md`, `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/STRUCTURE.md`, `.planning/codebase/CONVENTIONS.md`, and `.planning/codebase/TESTING.md`: document the resulting UI state flow, control location, and tests.

---

### Task 1: Scoped Progress and Unified Chart Busy State

**Files:**
- Modify: `SizeScanner.Avalonia.Tests/FakeScanService.cs:24-71`
- Modify: `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs:93-183`
- Modify: `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs:47-64,121-180`

**Interfaces:**
- Produces: `ChartViewModel.IsChartScanning : bool`
- Produces: `ChartViewModel.ScopeProgressValue : double`
- Produces: `ChartViewModel.IsScopeProgressIndeterminate : bool`
- Produces: observable `ChartViewModel.IsRootScanInProgress : bool`
- Produces: `FakeScanService.RootProgress` and `FakeScanService.ScopeProgress` test seams

- [ ] **Step 1: Let tests emit scan progress through the existing service contract**

In `FakeScanService`, capture each reporter before returning or awaiting:

```csharp
public IProgress<ScanProgress>? RootProgress { get; private set; }
public IProgress<ScanProgress>? ScopeProgress { get; private set; }

public Task<FsItem> RunAsync(
    string target,
    bool isDrive,
    CancellationToken cancellationToken,
    IProgress<ScanProgress> progress,
    ScanTreeBudget? budget = null)
{
    LastTarget = target;
    IsDriveScan = isDrive;
    RootCalls.Add((target, isDrive));
    RootProgress = progress;

    if (PendingRoot is not null)
        return PendingRoot.Task;

    cancellationToken.ThrowIfCancellationRequested();
    var result = RootResult?.Invoke(target, isDrive)
        ?? throw new InvalidOperationException("FakeScanService: no RootResult configured.");
    return Task.FromResult(result);
}

public Task<FsItem> RunScopeAsync(
    string target,
    CancellationToken cancellationToken,
    IProgress<ScanProgress> progress,
    ScanTreeBudget? budget = null,
    bool preferAllocatedSize = false)
{
    ScopeCalls.Add((target, preferAllocatedSize));
    ScopeProgress = progress;

    if (PendingScope is not null)
        return PendingScope.Task;

    cancellationToken.ThrowIfCancellationRequested();
    var result = ScopeResult?.Invoke(target)
        ?? throw new InvalidOperationException("FakeScanService: no ScopeResult configured.");
    return Task.FromResult(result);
}
```

- [ ] **Step 2: Write failing chart view-model tests**

Add these tests near the existing scoped-scan tests:

```csharp
[Fact]
public async Task Scope_scan_reports_progress_and_resets_transient_state_on_completion()
{
    var scan = new FakeScanService();
    var vm = CreateVm(scan);
    var root = SampleDriveRoot();
    vm.SetScan(root, isDrive: true, targetPath: "C:\\");
    vm.Refresh(0f, includeFreeSpace: false);
    var pending = new TaskCompletionSource<FsItem>(TaskCreationOptions.RunContinuationsAsynchronously);
    scan.PendingScope = pending;

    var scopeTask = vm.TryScopeAtAsync(root.Items![2]);

    Assert.True(vm.IsChartScanning);
    Assert.True(vm.IsScopeProgressIndeterminate);
    scan.ScopeProgress!.Report(new ScanProgress("C:\\Windows\\System32", 100, null, false));
    await Task.Yield();
    Assert.Equal("C:\\Windows\\System32", vm.ScopeStatusText);
    Assert.True(vm.IsScopeProgressIndeterminate);

    scan.ScopeProgress.Report(new ScanProgress("C:\\Windows\\System32", 200, 37.5f, false));
    await Task.Yield();
    Assert.Equal(37.5, vm.ScopeProgressValue);
    Assert.False(vm.IsScopeProgressIndeterminate);

    pending.SetResult(TestTree.Dir("Windows", TestTree.File("kernel.sys", 300)));
    Assert.True(await scopeTask);
    Assert.False(vm.IsChartScanning);
    Assert.Equal(0, vm.ScopeProgressValue);
    Assert.False(vm.IsScopeProgressIndeterminate);
    Assert.Empty(vm.ScopeStatusText);
}

[Fact]
public void IsChartScanning_includes_toolbar_root_scan_state()
{
    var vm = CreateVm();

    vm.IsRootScanInProgress = true;

    Assert.True(vm.IsChartScanning);

    vm.IsRootScanInProgress = false;

    Assert.False(vm.IsChartScanning);
}
```

Extend `Cancelled_scope_scan_leaves_chart_unchanged` with:

```csharp
Assert.False(vm.IsChartScanning);
Assert.Equal(0, vm.ScopeProgressValue);
Assert.False(vm.IsScopeProgressIndeterminate);
Assert.Empty(vm.ScopeStatusText);
```

- [ ] **Step 3: Run the focused tests and verify the new API is missing**

Run:

```powershell
rtk test dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~ChartViewModelTests"
```

Expected: FAIL to compile because `IsChartScanning`, `ScopeProgressValue`, and `IsScopeProgressIndeterminate` do not exist.

- [ ] **Step 4: Implement observable chart scan state and scoped progress**

Replace the plain root-scan property and annotate both scan flags:

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsChartScanning))]
private bool _isRootScanInProgress;

[ObservableProperty] private SunburstChart _layout = new([], 0, 0, 0);
[ObservableProperty] private bool _isScoped;
[ObservableProperty] private string _scopeLabel = string.Empty;
[ObservableProperty] private string _hoverPath = string.Empty;
[ObservableProperty] private string _hoverToolTip = string.Empty;
[ObservableProperty] private bool _isDeleting;
[ObservableProperty] private string _deleteStatusText = string.Empty;

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsChartScanning))]
private bool _isScopeScanning;

[ObservableProperty] private string _scopeStatusText = string.Empty;
[ObservableProperty] private double _scopeProgressValue;
[ObservableProperty] private bool _isScopeProgressIndeterminate;

public bool IsChartScanning => IsRootScanInProgress || IsScopeScanning;
```

Keep the existing XML documentation above `_isRootScanInProgress`, updating its
`<summary>` text to say it is also part of the chart's visual busy state.

Add a focused progress handler:

```csharp
private void OnScopeScanProgress(ScanProgress progress)
{
    ScopeStatusText = progress.CurrentPath;
    if (progress.PercentComplete.HasValue)
    {
        ScopeProgressValue = Math.Min(progress.PercentComplete.Value, 100);
        IsScopeProgressIndeterminate = false;
    }
    else
    {
        IsScopeProgressIndeterminate = true;
    }
}
```

Update `RunChartScanAsync` initialization, reporter, and cleanup:

```csharp
using var cts = new CancellationTokenSource();
_scopeCts = cts;
ScopeProgressValue = 0;
IsScopeProgressIndeterminate = true;
ScopeStatusText = string.Empty;
IsScopeScanning = true;
try
{
    return await scan(
        cts.Token,
        new Progress<ScanProgress>(OnScopeScanProgress));
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    return null;
}
catch (Exception ex)
{
    await _dialogs.ShowInfoAsync(failureTitle, ex.Message);
    return null;
}
finally
{
    _scopeCts = null;
    IsScopeScanning = false;
    ScopeProgressValue = 0;
    IsScopeProgressIndeterminate = false;
    ScopeStatusText = string.Empty;
}
```

- [ ] **Step 5: Run chart view-model tests**

Run:

```powershell
rtk test dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~ChartViewModelTests"
```

Expected: PASS.

- [ ] **Step 6: Inspect the focused diff**

Run:

```powershell
rtk git diff -- SizeScanner.Avalonia/ViewModels/ChartViewModel.cs SizeScanner.Avalonia.Tests/ChartViewModelTests.cs SizeScanner.Avalonia.Tests/FakeScanService.cs
```

Expected: only scoped progress, derived busy state, fake reporter capture, and focused tests.

---

### Task 2: Main-Window Unified Progress and Cleanup

**Files:**
- Modify: `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs:107-187,223-294`
- Modify: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs:67-81,101-120,195-299`
- Modify: `SizeScanner.Avalonia/Views/MainWindow.axaml:42-49`

**Interfaces:**
- Consumes: `ChartViewModel.ScopeProgressValue : double`
- Consumes: `ChartViewModel.IsScopeProgressIndeterminate : bool`
- Consumes: `ChartViewModel.IsScopeScanning : bool`
- Produces: `MainWindowViewModel.DisplayProgressValue : double`
- Produces: `MainWindowViewModel.DisplayProgressIsIndeterminate : bool`

- [ ] **Step 1: Write failing unified progress tests**

Add:

```csharp
[Fact]
public async Task Scoped_scan_drives_main_window_progress_presentation()
{
    var scan = new FakeScanService();
    var chart = new ChartViewModel(scan, new NoopFs(), new NoopDialogs());
    var root = TestTree.Dir("C:\\", TestTree.Dir("Data", TestTree.File("f.bin", 10)));
    var vm = CreateVm(root, chart: chart, scan: scan);
    await vm.ScanTargetAsync("C:\\", isDrive: false);
    var pending = new TaskCompletionSource<FsItem>(TaskCreationOptions.RunContinuationsAsynchronously);
    scan.PendingScope = pending;

    var scopeTask = chart.TryScopeAtAsync(root.Items![0]);

    Assert.True(vm.IsBusy);
    Assert.True(vm.DisplayProgressIsIndeterminate);
    Assert.Equal(0, vm.DisplayProgressValue);

    scan.ScopeProgress!.Report(new ScanProgress("C:\\Data", 20, 42f, false));
    await Task.Yield();

    Assert.False(vm.DisplayProgressIsIndeterminate);
    Assert.Equal(42, vm.DisplayProgressValue);
    Assert.Contains("C:\\Data", vm.DisplayStatusText);

    pending.SetResult(TestTree.Dir("Data", TestTree.File("f.bin", 10)));
    Assert.True(await scopeTask);
    Assert.False(vm.IsBusy);
    Assert.False(vm.DisplayProgressIsIndeterminate);
    Assert.Equal(0, vm.DisplayProgressValue);
}

[Fact]
public async Task Root_scan_without_percentage_uses_indeterminate_progress()
{
    var scan = new FakeScanService();
    var pending = new TaskCompletionSource<FsItem>(TaskCreationOptions.RunContinuationsAsynchronously);
    scan.PendingRoot = pending;
    var vm = CreateVm(DriveRoot(), scan: scan);

    var scanTask = vm.ScanTargetAsync("D:\\data", isDrive: false);
    scan.RootProgress!.Report(new ScanProgress("D:\\data\\child", 100, null, false));
    await Task.Yield();

    Assert.True(vm.DisplayProgressIsIndeterminate);
    Assert.Equal(0, vm.DisplayProgressValue);

    pending.SetResult(DriveRoot());
    await scanTask;
    Assert.False(vm.DisplayProgressIsIndeterminate);
}

[Fact]
public async Task Failed_root_scan_clears_busy_progress_state()
{
    var scan = new FakeScanService();
    var pending = new TaskCompletionSource<FsItem>(TaskCreationOptions.RunContinuationsAsynchronously);
    scan.PendingRoot = pending;
    var vm = CreateVm(DriveRoot(), scan: scan);
    var scanTask = vm.ScanTargetAsync("D:\\data", isDrive: false);

    pending.SetException(new IOException("boom"));

    await Assert.ThrowsAsync<IOException>(() => scanTask);
    Assert.False(vm.IsBusy);
    Assert.False(vm.Chart.IsChartScanning);
    Assert.False(vm.DisplayProgressIsIndeterminate);
    Assert.Equal(0, vm.DisplayProgressValue);
    Assert.Empty(vm.StatusDetails);
    Assert.Equal("Scan failed", vm.StatusText);
}
```

Add `using System.IO;` to `MainWindowViewModelTests.cs`.

- [ ] **Step 2: Run the focused tests and verify the display API is missing**

Run:

```powershell
rtk test dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~MainWindowViewModelTests"
```

Expected: FAIL to compile because the unified display progress properties do not exist.

- [ ] **Step 3: Add root and computed display progress properties**

Annotate the existing progress value and add root indeterminate state:

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(DisplayProgressValue))]
private double _progressValue;

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(DisplayProgressIsIndeterminate))]
private bool _isProgressIndeterminate;
```

Add:

```csharp
public double DisplayProgressValue =>
    Chart.IsScopeScanning ? Chart.ScopeProgressValue : ProgressValue;

public bool DisplayProgressIsIndeterminate =>
    Chart.IsScopeScanning
        ? Chart.IsScopeProgressIndeterminate
        : IsProgressIndeterminate;
```

Update root progress handling:

```csharp
private void OnScanProgress(ScanProgress progress)
{
    if (progress.PercentComplete.HasValue)
    {
        ProgressValue = Math.Min(progress.PercentComplete.Value, 100);
        IsProgressIndeterminate = false;
    }
    else
    {
        IsProgressIndeterminate = true;
    }

    StatusDetails = progress.CurrentPath;
}
```

- [ ] **Step 4: Propagate chart progress property changes**

Extend `OnChartPropertyChanged`:

```csharp
if (e.PropertyName is nameof(ChartViewModel.IsDeleting) or nameof(ChartViewModel.DeleteStatusText)
    or nameof(ChartViewModel.IsScopeScanning) or nameof(ChartViewModel.ScopeStatusText))
{
    OnPropertyChanged(nameof(DisplayStatusText));
    OnPropertyChanged(nameof(HoverStatusVisible));
}

if (e.PropertyName is nameof(ChartViewModel.IsScopeScanning)
    or nameof(ChartViewModel.ScopeProgressValue)
    or nameof(ChartViewModel.IsScopeProgressIndeterminate))
{
    OnPropertyChanged(nameof(DisplayProgressValue));
    OnPropertyChanged(nameof(DisplayProgressIsIndeterminate));
}
```

Retain the existing `IsScopeScanning` command-notification block.

- [ ] **Step 5: Make root scan cleanup unconditional**

Refactor `ScanTargetAsync` so initialization sets
`IsProgressIndeterminate = true`, successful processing stays in the `try`, and
all transient state is cleared in `finally`:

```csharp
public async Task ScanTargetAsync(string target, bool isDrive)
{
    if (IsBusy) return;

    _scanCts?.Dispose();
    var cts = new CancellationTokenSource();
    _scanCts = cts;
    var token = cts.Token;

    SetScanningState(true);
    Chart.IsRootScanInProgress = true;
    StatusText = $"Scanning {target}...";
    StatusDetails = string.Empty;
    ProgressValue = 0;
    IsProgressIndeterminate = true;

    try
    {
        var progress = new Progress<ScanProgress>(OnScanProgress);
        var root = await _scan.RunAsync(target, isDrive, token, progress);
        token.ThrowIfCancellationRequested();

        _scanRoot = root;
        PopulateInaccessible(isDrive);
        Chart.SetScan(root, isDrive, _scan.Scanner.CurrentTarget ?? target);
        RefreshChart();

        CanRescan = true;
        RescanCommand.NotifyCanExecuteChanged();
        StatusText = "Ready";
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
        StatusText = "Scan cancelled";
    }
    catch
    {
        StatusText = "Scan failed";
        throw;
    }
    finally
    {
        Chart.IsRootScanInProgress = false;
        ProgressValue = 0;
        IsProgressIndeterminate = false;
        StatusDetails = string.Empty;
        SetScanningState(false);

        if (ReferenceEquals(_scanCts, cts))
            _scanCts = null;
        cts.Dispose();
    }
}
```

Remove `FinishCancelled`, which is no longer called. The `IsBusy` guard also
strengthens the direct-call backstop so public callers cannot overlap root
scans.

- [ ] **Step 6: Bind the toolbar controls to unified state**

Change the progress section in `MainWindow.axaml` to:

```xml
<Separator Height="NaN" Width="1" IsVisible="{Binding IsBusy}" />
<ProgressBar Width="280" Minimum="0" Maximum="100"
             Value="{Binding DisplayProgressValue}"
             IsIndeterminate="{Binding DisplayProgressIsIndeterminate}"
             IsVisible="{Binding IsBusy}" VerticalAlignment="Center" />
<Button Content="Cancel" Command="{Binding CancelScanCommand}"
        IsVisible="{Binding IsBusy}" />
```

- [ ] **Step 7: Run main-window tests and compile AXAML**

Run:

```powershell
rtk test dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~MainWindowViewModelTests"
rtk dotnet build SizeScanner.Avalonia/SizeScanner.Avalonia.csproj -c Debug
```

Expected: both commands PASS; compiled bindings resolve both display progress properties.

- [ ] **Step 8: Inspect the focused diff**

Run:

```powershell
rtk git diff -- SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs SizeScanner.Avalonia/Views/MainWindow.axaml SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs
```

Expected: unified progress selection, unconditional root cleanup, toolbar bindings, and tests only.

---

### Task 3: Dependency-Free Chart Spinner Overlay

**Files:**
- Create: `SizeScanner.Avalonia/Views/BusySpinnerControl.cs`
- Modify: `SizeScanner.Avalonia/Views/ChartView.axaml:8-43`
- Modify: `SizeScanner.Avalonia.Tests/ViewLocatorTests.cs:30-40`

**Interfaces:**
- Consumes: `ChartViewModel.IsChartScanning : bool`
- Produces: `BusySpinnerControl.IsActive : bool`
- Produces: `BusySpinnerControl.TrackBrush : IBrush?`
- Produces: `BusySpinnerControl.IndicatorBrush : IBrush?`

- [ ] **Step 1: Write a failing chart-view composition test**

Add this test to `ViewLocatorTests`:

```csharp
[Fact]
public void ChartView_contains_app_owned_busy_spinner()
{
    var view = new ChartView();

    var spinner = view.FindControl<BusySpinnerControl>("PART_ScanSpinner");

    Assert.NotNull(spinner);
}
```

- [ ] **Step 2: Run the focused test and verify the control is missing**

Run:

```powershell
rtk test dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~ViewLocatorTests"
```

Expected: FAIL to compile because `BusySpinnerControl` does not exist.

- [ ] **Step 3: Implement the circular spinner control**

Create `BusySpinnerControl.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SizeScanner.Avalonia.Views;

public sealed class BusySpinnerControl : Control
{
    private const double StrokeThickness = 4d;
    private const double IndicatorSweep = 110d;
    private const double DegreesPerTick = 18d;

    private readonly DispatcherTimer _timer;
    private bool _isAttached;
    private double _startAngle;

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<BusySpinnerControl, bool>(nameof(IsActive));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<BusySpinnerControl, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> IndicatorBrushProperty =
        AvaloniaProperty.Register<BusySpinnerControl, IBrush?>(nameof(IndicatorBrush));

    public BusySpinnerControl()
    {
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Render,
            (_, _) =>
            {
                _startAngle = (_startAngle + DegreesPerTick) % 360d;
                InvalidateVisual();
            });
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? IndicatorBrush
    {
        get => GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    static BusySpinnerControl()
    {
        AffectsRender<BusySpinnerControl>(
            IsActiveProperty,
            TrackBrushProperty,
            IndicatorBrushProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
            UpdateTimer();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!IsActive)
            return;

        var diameter = Math.Min(Bounds.Width, Bounds.Height);
        var radius = Math.Max(0, diameter / 2d - StrokeThickness / 2d);
        if (radius <= 0)
            return;

        var center = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        context.DrawEllipse(
            null,
            new Pen(TrackBrush ?? Brushes.Gray, StrokeThickness),
            center,
            radius,
            radius);

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(PointOnCircle(center, radius, _startAngle), isFilled: false);
            path.ArcTo(
                PointOnCircle(center, radius, _startAngle + IndicatorSweep),
                new Size(radius, radius),
                0,
                isLargeArc: false,
                SweepDirection.Clockwise);
            path.EndFigure(isClosed: false);
        }

        context.DrawGeometry(
            null,
            new Pen(IndicatorBrush ?? Brushes.White, StrokeThickness),
            geometry);
    }

    private void UpdateTimer()
    {
        if (_isAttached && IsActive)
            _timer.Start();
        else
            _timer.Stop();

        if (!IsActive)
            _startAngle = 0;
        InvalidateVisual();
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = (angle - 90d) * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
```

- [ ] **Step 4: Layer the spinner over the chart**

Keep the scope header in row 0. Replace the single chart control in row 1 with:

```xml
<Grid Grid.Row="1">
    <views:SunburstChartControl x:Name="PART_Chart"
                                HorizontalAlignment="Stretch"
                                VerticalAlignment="Stretch"
                                Chart="{Binding Layout}"
                                Focusable="True"
                                ToolTip.ShowDelay="100">
        <views:SunburstChartControl.ContextMenu>
            <ContextMenu>
                <MenuItem Header="Open in Explorer" Command="{Binding OpenInExplorerCommand}" />
                <MenuItem Header="Delete" Command="{Binding DeleteCommand}" />
                <MenuItem Header="Delete permanently" Command="{Binding DeletePermanentlyCommand}" />
            </ContextMenu>
        </views:SunburstChartControl.ContextMenu>
        <ToolTip.Tip>
            <ToolTip MaxWidth="{x:Static sys:Double.PositiveInfinity}" Padding="6">
                <TextBlock Text="{Binding HoverToolTip}"
                           FontFamily="Consolas,Courier New,monospace"
                           TextWrapping="NoWrap" />
            </ToolTip>
        </ToolTip.Tip>
    </views:SunburstChartControl>

    <Grid Background="Transparent"
          IsVisible="{Binding IsChartScanning}">
        <Border Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}"
                Opacity="0.72" />
        <views:BusySpinnerControl x:Name="PART_ScanSpinner"
                                  Width="52" Height="52"
                                  HorizontalAlignment="Center"
                                  VerticalAlignment="Center"
                                  IsActive="{Binding IsChartScanning}"
                                  TrackBrush="{DynamicResource SystemControlForegroundChromeDisabledLowBrush}"
                                  IndicatorBrush="{DynamicResource SystemControlForegroundBaseHighBrush}"
                                  AutomationProperties.Name="Scanning" />
    </Grid>
</Grid>
```

The top grid has a transparent background so it participates in hit testing and
prevents clicks from reaching `SunburstChartControl` during a scan.

- [ ] **Step 5: Run the view test and compile the application**

Run:

```powershell
rtk test dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~ViewLocatorTests"
rtk dotnet build SizeScanner.Avalonia/SizeScanner.Avalonia.csproj -c Debug
```

Expected: PASS with no AXAML binding or control errors.

- [ ] **Step 6: Inspect the focused diff**

Run:

```powershell
rtk git diff -- SizeScanner.Avalonia/Views/BusySpinnerControl.cs SizeScanner.Avalonia/Views/ChartView.axaml SizeScanner.Avalonia.Tests/ViewLocatorTests.cs
```

Expected: one focused rendering control, one overlay, and one composition test.

---

### Task 4: Documentation and Full Verification

**Files:**
- Modify: `AGENTS.md`
- Modify: `.planning/codebase/ARCHITECTURE.md`
- Modify: `.planning/codebase/STRUCTURE.md`
- Modify: `.planning/codebase/CONVENTIONS.md`
- Modify: `.planning/codebase/TESTING.md`

**Interfaces:**
- Consumes: final production and test behavior from Tasks 1-3
- Produces: repository guidance aligned with unified progress and spinner behavior

- [ ] **Step 1: Update repository guidance**

Make these exact documentation changes:

- `AGENTS.md`: state that root and scoped scans both drive the toolbar
  progress/cancel UI; percentage-less directory scans are indeterminate; and
  `ChartView` overlays `BusySpinnerControl` for every scan. Add
  `SizeScanner.Avalonia/Views/BusySpinnerControl.cs` to key files.
- `.planning/codebase/ARCHITECTURE.md`: update Main Window, Main orchestration
  VM, Chart interaction VM, and Views responsibilities to describe unified
  presentation state and the pointer-blocking chart overlay.
- `.planning/codebase/STRUCTURE.md`: add `BusySpinnerControl.cs` under Views and
  identify `ChartView.axaml` as the scan overlay owner.
- `.planning/codebase/CONVENTIONS.md`: record the existing-owner/computed-display
  pattern for cross-view-model progress and the rule that timer-backed controls
  stop timers when detached or inactive.
- `.planning/codebase/TESTING.md`: list scoped progress, unified busy state,
  cleanup, and chart-view spinner composition among Avalonia test coverage.

Do not change `.planning/codebase/STACK.md` or `INTEGRATIONS.md`: this feature
adds no dependency or integration. Do not change `CONCERNS.md` unless
implementation reveals a new unresolved risk.

- [ ] **Step 2: Check lints for all edited source and AXAML files**

Use the IDE lint reader on:

```text
SizeScanner.Avalonia/ViewModels/ChartViewModel.cs
SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs
SizeScanner.Avalonia/Views/BusySpinnerControl.cs
SizeScanner.Avalonia/Views/MainWindow.axaml
SizeScanner.Avalonia/Views/ChartView.axaml
SizeScanner.Avalonia.Tests/FakeScanService.cs
SizeScanner.Avalonia.Tests/ChartViewModelTests.cs
SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs
SizeScanner.Avalonia.Tests/ViewLocatorTests.cs
```

Expected: no newly introduced diagnostics.

- [ ] **Step 3: Run the complete Avalonia test project**

Run:

```powershell
rtk test dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release
```

Expected: PASS.

- [ ] **Step 4: Build the complete solution**

Run:

```powershell
rtk dotnet build SizeScanner.slnx -c Debug
rtk dotnet build SizeScanner.slnx -c Release
```

Expected: both builds PASS.

- [ ] **Step 5: Perform the Native AOT/trimming packaging check**

Run:

```powershell
rtk dotnet publish SizeScanner.Avalonia/SizeScanner.Avalonia.csproj -c Release -r win-x64
```

Expected: PASS without trimming or AOT warnings caused by the new control or bindings.

- [ ] **Step 6: Review final scope and working tree**

Run:

```powershell
rtk git diff --check
rtk git status --short
rtk git diff -- SizeScanner.Avalonia SizeScanner.Avalonia.Tests AGENTS.md .planning/codebase docs/superpowers/specs/2026-08-06-scan-progress-spinner-design.md docs/superpowers/plans/2026-08-06-scan-progress-spinner.md
```

Expected: no whitespace errors; only the requested feature, tests, design/plan,
and required documentation are new or modified. Keep the pre-existing staged
shallow-fanout files untouched.
