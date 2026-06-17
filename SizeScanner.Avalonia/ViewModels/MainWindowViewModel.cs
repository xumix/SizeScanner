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
    [NotifyPropertyChangedFor(nameof(HoverStatusVisible))]
    private bool _isScanning;

    [ObservableProperty] private bool _canRescan;
    [ObservableProperty] private double _progressValue;

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

    public string DisplayStatusText => Chart.IsDeleting ? Chart.DeleteStatusText : StatusText;

    public bool HoverStatusVisible => !IsScanning && !Chart.IsDeleting;

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

    [RelayCommand]
    private async Task ScanDriveAsync(DriveItem? drive)
    {
        if (drive is not null)
            await ScanTargetAsync(drive.Root, isDrive: true);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _folderPicker.PickFolderAsync("Select a folder to scan");
        if (!string.IsNullOrEmpty(path))
            await ScanTargetAsync(path, isDrive: false);
    }

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync()
    {
        if (!string.IsNullOrEmpty(_scan.LastTarget))
            await ScanTargetAsync(_scan.LastTarget, _scan.IsDriveScan);
    }

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

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
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        SetScanningState(true);
        StatusText = $"Scanning {target}...";
        StatusDetails = string.Empty;
        ProgressValue = 0;

        var progress = new Progress<ScanProgress>(OnScanProgress);
        FsItem root;
        try
        {
            root = await _scan.RunAsync(target, isDrive, token, progress);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            FinishCancelled();
            return;
        }

        if (token.IsCancellationRequested) { FinishCancelled(); return; }

        _scanRoot = root;
        PopulateInaccessible(isDrive);

        Chart.SetScan(root, isDrive, _scan.Scanner.CurrentTarget ?? target);
        RefreshChart();

        CanRescan = true;
        RescanCommand.NotifyCanExecuteChanged();
        ProgressValue = 0;
        StatusText = "Ready";
        StatusDetails = string.Empty;
        SetScanningState(false);
        _scanCts.Dispose();
        _scanCts = null;
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

    private void OnScanProgress(ScanProgress p)
    {
        if (p.PercentComplete.HasValue)
            ProgressValue = Math.Min(p.PercentComplete.Value, 100);
        StatusDetails = p.CurrentPath;
    }

    private void SetScanningState(bool scanning) => IsScanning = scanning;

    private void OnChartPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartViewModel.IsDeleting) or nameof(ChartViewModel.DeleteStatusText))
        {
            OnPropertyChanged(nameof(DisplayStatusText));
            OnPropertyChanged(nameof(HoverStatusVisible));
        }
    }

    private void FinishCancelled()
    {
        ProgressValue = 0;
        StatusText = "Scan cancelled";
        StatusDetails = string.Empty;
        SetScanningState(false);
        _scanCts?.Dispose();
        _scanCts = null;
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
