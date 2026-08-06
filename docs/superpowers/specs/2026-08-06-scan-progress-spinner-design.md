# Unified Scan Progress and Chart Spinner Design

**Date:** 2026-08-06
**Status:** Approved

## Goal

Show the existing toolbar progress UI when clicking a chart sector starts a
scoped directory scan, and show a centered spinner over `ChartView` during every
root or scoped scan.

## User-visible behavior

- The toolbar progress bar and Cancel button are visible whenever either a
  toolbar scan or chart-initiated scoped scan is active.
- Drive scans remain determinate when `ScanProgress.PercentComplete` is known.
- Directory scans are indeterminate because their final total is unknown until
  traversal completes. Their current path continues to update in the status
  area.
- A centered circular spinner appears over the chart during root and scoped
  scans. A translucent overlay lightly dims the current chart and prevents new
  chart interactions while the scan is active.
- The previous chart remains visible until a successful scan replaces it.
- Completion, cancellation, and failure clear the progress bar, Cancel button,
  spinner, and transient progress text.

## Architecture

Scan ownership remains unchanged:

- `MainWindowViewModel` owns toolbar/root scan progress.
- `ChartViewModel` owns scoped scan progress.
- `IScanService` continues to pass `IProgress<ScanProgress>` into both scan
  paths.

`ChartViewModel` will expose observable scoped progress and a derived
`IsChartScanning` property that combines its existing root-scan coordination
flag with `IsScopeScanning`. `IsRootScanInProgress` must become observable so
the chart overlay reacts to toolbar scans.

`MainWindowViewModel` will expose display progress properties that select the
active source:

- Root scan: use its existing `ProgressValue` and new root indeterminate state.
- Scoped scan: use `ChartViewModel`'s scoped progress value and indeterminate
  state.

The main window observes chart progress property changes in the same
`PropertyChanged` bridge already used for scoped status and busy state. This
keeps the view models independent while giving the window one binding surface.

## Progress data flow

At scan start, reset the relevant progress value and assume indeterminate until
a report supplies `PercentComplete`.

For each `ScanProgress` report:

1. Update the current path.
2. If `PercentComplete` has a value, clamp it to 100, assign the progress value,
   and make the bar determinate.
3. If it has no value, keep the bar indeterminate.

`MainWindow.axaml` will bind progress visibility and Cancel visibility to
`IsBusy`, the value to the unified display value, and `IsIndeterminate` to the
unified display mode.

## Chart spinner

`ChartView.axaml` will add an overlay above `SunburstChartControl`, bound to
`ChartViewModel.IsChartScanning`. The overlay contains an app-owned circular
busy spinner implemented with Avalonia drawing/animation primitives under
`SizeScanner.Avalonia/Views`; no third-party package is introduced.

The overlay fills the chart row so it receives pointer input while active. The
scope navigation header remains visible, while its commands continue to be
guarded by scan state in the view model.

## Cancellation and errors

The existing Escape/Cancel command cancels whichever root or scoped token source
is active. Progress state is reset from `finally` paths so cancellation and
exceptions cannot leave the UI busy. A failed or cancelled scoped scan leaves
the existing chart unchanged.

## Testing

Add focused view-model tests for:

- Scoped progress reports update the exposed path, value, and determinate state.
- A scoped report without a percentage selects indeterminate progress.
- Unified main-window progress visibility/state follows scoped scans.
- `IsChartScanning` is true for root and scoped scans and false after each ends.
- Completion, cancellation, and failure reset transient busy/progress state.

Build and run the Avalonia test project, then build the solution to compile the
AXAML and verify Native AOT-compatible bindings. Update `AGENTS.md` and affected
documents under `.planning/codebase/` to reflect the unified progress and chart
overlay behavior.
