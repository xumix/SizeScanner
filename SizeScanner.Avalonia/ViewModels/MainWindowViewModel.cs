// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Charting;
using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    public const int DefaultInaccessiblePaneWidth = 360;

    private readonly IScanService _scan;
    private readonly ISettingsStore _settingsStore;
    private readonly IDriveProvider _driveProvider;
    private readonly IElevationService _elevation;
    private readonly IFolderPicker _folderPicker;
    private readonly UserSettings _settings;

    private FsItem? _scanRoot;
    private CancellationTokenSource? _scanCts;
    private bool _suppressOptionChanges;
    private bool _initialized;

    public MainWindowViewModel(
        IScanService scan,
        ISettingsStore settingsStore,
        IDriveProvider driveProvider,
        IElevationService elevation,
        IFolderPicker folderPicker,
        ChartViewModel chart)
    {
        _scan = scan;
        _settingsStore = settingsStore;
        _driveProvider = driveProvider;
        _elevation = elevation;
        _folderPicker = folderPicker;
        Chart = chart;
        Chart.PropertyChanged += OnChartPropertyChanged;
        Chart.RootRescanned += OnChartRootRescanned;
        _settings = _settingsStore.Load();

        for (var i = 0; i <= 8; i++)
            FilterOptions.Add(FilterLabels[i]);
    }

    public ChartViewModel Chart { get; }

    public ObservableCollection<DriveItem> Drives { get; } = [];
    public ObservableCollection<string> FreeSpaceOptions { get; } = ["Show free space", "Hide free space"];
    public ObservableCollection<string> FilterOptions { get; } = [];
    public ObservableCollection<string> InaccessiblePaths { get; } = [];
    public int InitialWindowWidth => _settings.WindowWidth;
    public int InitialWindowHeight => _settings.WindowHeight;

    [ObservableProperty] private int _filterIndex = 4;
    [ObservableProperty] private int _freeSpaceIndex = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoverStatusVisible), nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ScanDriveCommand), nameof(BrowseCommand), nameof(RescanCommand))]
    private bool _isScanning;

    [ObservableProperty] private bool _canRescan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayProgressValue))]
    private double _progressValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayProgressIsIndeterminate))]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatusText))]
    private string _statusText = "Ready";

    [ObservableProperty] private string _statusDetails = string.Empty;
    [ObservableProperty] private string _inaccessibleTotalSize = Humanize.Size(0);
    [ObservableProperty] private bool _relaunchAsAdminVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InaccessiblePaneVisible), nameof(InaccessiblePaneColumnWidth), nameof(InaccessiblePaneColumnMinWidth))]
    private bool _inaccessiblePaneCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InaccessiblePaneColumnWidth))]
    private double _inaccessiblePaneWidth = DefaultInaccessiblePaneWidth;

    public bool InaccessiblePaneVisible => !InaccessiblePaneCollapsed;

    public GridLength InaccessiblePaneColumnWidth =>
        InaccessiblePaneVisible ? new GridLength(InaccessiblePaneWidth) : new GridLength(0);

    public double InaccessiblePaneColumnMinWidth =>
        InaccessiblePaneVisible ? DefaultInaccessiblePaneWidth : 0;

    public string DisplayStatusText =>
        Chart.IsDeleting ? Chart.DeleteStatusText
        : Chart.IsScopeScanning ? BuildScopeScanningStatusText()
        : StatusText;

    public bool HoverStatusVisible => !IsBusy && !Chart.IsDeleting;

    /// <summary>
    /// Unified toolbar progress presentation: while the chart is running a scope-side scan
    /// (drill-down/"Go up"/stale-root refresh), its progress drives the toolbar bar so the
    /// user sees one progress indicator regardless of which scan is in flight. Otherwise the
    /// toolbar's own root-scan progress is shown.
    /// </summary>
    public double DisplayProgressValue =>
        Chart.IsScopeScanning ? Chart.ScopeProgressValue : ProgressValue;

    public bool DisplayProgressIsIndeterminate =>
        Chart.IsScopeScanning
            ? Chart.IsScopeProgressIndeterminate
            : IsProgressIndeterminate;

    /// <summary>
    /// A scan is in flight, from either the toolbar or a scope-side rescan. Backs toolbar
    /// IsEnabled bindings, and gates every scan-start action via <see cref="CanStartScan"/>:
    /// a stale "Go to root"/"Go up" rescan also calls the shared
    /// <see cref="IScanService.RunAsync"/>, so the toolbar must stay disabled for that too or
    /// two RunAsync calls can race the same non-thread-safe DriveScanner.
    /// </summary>
    public bool IsBusy => IsScanning || Chart.IsScopeScanning;

    private string BuildScopeScanningStatusText() =>
        string.IsNullOrEmpty(Chart.ScopeStatusText)
            ? "Scanning..."
            : $"Scanning {Chart.ScopeStatusText}...";

    private static readonly string[] FilterLabels =
    [
        "No threshold", "0.25%", "0.5%", "0.75%", "1%",
        "1.25%", "1.5%", "1.75% (Rougher)", "2% (ROUGH!)"
    ];

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var drive in _driveProvider.GetReadyDrives())
            Drives.Add(drive);

        _suppressOptionChanges = true;
        FreeSpaceIndex = _settings.FreeSpaceIndex;
        FilterIndex = _settings.FilterIndex;
        InaccessiblePaneCollapsed = _settings.InaccessiblePaneCollapsed;
        InaccessiblePaneWidth = _settings.SplitterDistance > 0
            ? _settings.SplitterDistance
            : DefaultInaccessiblePaneWidth;
        _suppressOptionChanges = false;
    }

    private bool CanStartScan() => !IsBusy;

    private bool CanExecuteRescan() => CanRescan && CanStartScan();

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task ScanDriveAsync(DriveItem? drive)
    {
        if (drive is not null)
            await ScanTargetAsync(drive.Root, isDrive: true);
    }

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task BrowseAsync()
    {
        var path = await _folderPicker.PickFolderAsync("Select a folder to scan");
        if (!string.IsNullOrEmpty(path))
            await ScanTargetAsync(path, isDrive: false);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteRescan))]
    private async Task RescanAsync()
    {
        if (!string.IsNullOrEmpty(_scan.LastTarget))
            await ScanTargetAsync(_scan.LastTarget, _scan.IsDriveScan);
    }

    [RelayCommand]
    private void CancelScan()
    {
        if (_scanCts is not null)
            _scanCts.Cancel();
        else
            Chart.CancelScopeScan();
    }

    [RelayCommand]
    private void ToggleInaccessiblePane()
    {
        InaccessiblePaneCollapsed = !InaccessiblePaneCollapsed;
        Persist();
    }

    [RelayCommand]
    private void RelaunchAsAdmin()
    {
        if (_elevation.TryRelaunchAsAdministrator(out _))
            Environment.Exit(0);
    }

    public async Task ScanTargetAsync(string target, bool isDrive)
    {
        // Defense in depth alongside CanStartScan()/CanExecuteRescan(): refuse to start a
        // second RunAsync while another root or scope-side rescan is racing the shared
        // ScanService.
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

    private void PopulateInaccessible(bool isDrive)
    {
        InaccessiblePaths.Clear();
        foreach (var path in _scan.Scanner.Inaccessible)
            InaccessiblePaths.Add(path);

        InaccessibleTotalSize = isDrive && _scanRoot is not null
            ? Humanize.Size(DriveScanMetadata.GetInaccessibleEntry(_scanRoot).Size)
            : Humanize.Size(0);

        RelaunchAsAdminVisible = InaccessiblePaths.Count > 0 && !_elevation.IsRunningAsAdministrator();
    }

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

    private void SetScanningState(bool scanning) => IsScanning = scanning;

    private void OnChartPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartViewModel.IsDeleting) or nameof(ChartViewModel.DeleteStatusText)
            or nameof(ChartViewModel.IsScopeScanning) or nameof(ChartViewModel.ScopeStatusText))
        {
            OnPropertyChanged(nameof(DisplayStatusText));
            OnPropertyChanged(nameof(HoverStatusVisible));
        }

        if (e.PropertyName == nameof(ChartViewModel.IsScopeScanning))
        {
            OnPropertyChanged(nameof(IsBusy));
            ScanDriveCommand.NotifyCanExecuteChanged();
            BrowseCommand.NotifyCanExecuteChanged();
            RescanCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(ChartViewModel.IsScopeScanning)
            or nameof(ChartViewModel.ScopeProgressValue)
            or nameof(ChartViewModel.IsScopeProgressIndeterminate))
        {
            OnPropertyChanged(nameof(DisplayProgressValue));
            OnPropertyChanged(nameof(DisplayProgressIsIndeterminate));
        }
    }

    private void OnChartRootRescanned(FsItem root)
    {
        _scanRoot = root;
        PopulateInaccessible(_scan.IsDriveScan);
    }

    private void RefreshChart()
    {
        if (_scanRoot is null) return;
        var percent = FilterThreshold.PercentFromIndex(FilterIndex);
        var includeFreeSpace = FreeSpaceIndex == 0;
        Chart.Refresh(percent, includeFreeSpace);
    }

    partial void OnFilterIndexChanged(int value) => OnDisplayOptionsChanged();
    partial void OnFreeSpaceIndexChanged(int value) => OnDisplayOptionsChanged();

    private void OnDisplayOptionsChanged()
    {
        if (_suppressOptionChanges || IsScanning || _scanRoot is null) return;
        RefreshChart();
        Persist();
    }

    public UserSettings CaptureSettings(int windowWidth, int windowHeight, int splitterDistance)
    {
        _settings.FilterIndex = FilterIndex;
        _settings.FreeSpaceIndex = FreeSpaceIndex;
        _settings.WindowWidth = windowWidth;
        _settings.WindowHeight = windowHeight;
        _settings.SplitterDistance = splitterDistance;
        _settings.InaccessiblePaneCollapsed = InaccessiblePaneCollapsed;
        return _settings;
    }

    public void SaveOnClose(int windowWidth, int windowHeight, int splitterDistance) =>
        _settingsStore.Save(CaptureSettings(windowWidth, windowHeight, splitterDistance));

    private void Persist()
    {
        _settings.FilterIndex = FilterIndex;
        _settings.FreeSpaceIndex = FreeSpaceIndex;
        _settings.InaccessiblePaneCollapsed = InaccessiblePaneCollapsed;
        _settingsStore.Save(_settings);
    }
}
