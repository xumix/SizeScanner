// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Charting;

namespace SizeScanner.Avalonia.ViewModels;

public sealed partial class ChartViewModel : ViewModelBase
{
    private readonly SunburstChartBuilder _builder = new();
    private readonly IFileSystemActions _fileSystem;
    private readonly IDialogService _dialogs;

    private FsItem? _scanRoot;
    private FsItem? _chartRootWithoutSynthetic;
    private bool _isDriveScan;
    private bool _includeFreeSpace;
    private float _filterPercent;
    private string _targetPath = string.Empty;
    private FsItem? _scopedRoot;
    private string _displayRootPath = string.Empty;

    public ChartViewModel(IFileSystemActions fileSystem, IDialogService dialogs)
    {
        _fileSystem = fileSystem;
        _dialogs = dialogs;
    }

    [ObservableProperty] private SunburstChart _layout = new([], 0, 0);
    [ObservableProperty] private bool _isScoped;
    [ObservableProperty] private string _scopeLabel = string.Empty;
    [ObservableProperty] private string _hoverPath = string.Empty;
    [ObservableProperty] private string _hoverToolTip = string.Empty;

    public FsItem? ContextTarget { get; private set; }
    public string ContextTargetPath { get; private set; } = string.Empty;

    public void SetScan(FsItem scanRoot, bool isDrive, string targetPath)
    {
        _scanRoot = scanRoot;
        _isDriveScan = isDrive;
        _targetPath = targetPath;
        _chartRootWithoutSynthetic = BuildChartRootWithoutSyntheticEntries(scanRoot, isDrive);
        _scopedRoot = null;
        UpdateScopeState();
    }

    public void Refresh(float filterPercent, bool includeFreeSpace)
    {
        _filterPercent = filterPercent;
        _includeFreeSpace = includeFreeSpace;
        if (_scanRoot is null)
        {
            Layout = new SunburstChart([], 0, 0);
            return;
        }

        var displayRoot = GetDisplayRoot();
        var threshold = FilterThreshold.Compute(_filterPercent, displayRoot);
        Layout = _builder.Build(displayRoot, threshold);
    }

    public FsItem? ResolveNode(SunburstSegment? segment) => segment?.Node;

    public void Hover(FsItem? node)
    {
        if (node is null) { ClearHover(); return; }

        var chain = AncestorChain(node);
        HoverPath = BuildFullPath(chain);
        HoverToolTip = ChartHoverToolTip.BuildText(chain);
    }

    public void ClearHover()
    {
        HoverPath = string.Empty;
        HoverToolTip = string.Empty;
    }

    public bool CanScopeTo(FsItem? item)
    {
        if (item is null || !item.IsDir) return false;
        if (item.Items is null || item.Items.Count == 0) return false;
        if (item.Name == DriveScanMetadata.FreeSpaceName
            || item.Name == DriveScanMetadata.InaccessibleName
            || item.Name == ChartDisplayMetadata.FilteredName)
            return false;
        return true;
    }

    public bool TryScopeAt(FsItem node)
    {
        if (!CanScopeTo(node)) return false;
        _scopedRoot = node;
        UpdateScopeState();
        Refresh(_filterPercent, _includeFreeSpace);
        return true;
    }

    [RelayCommand]
    private void GoUp()
    {
        if (_scopedRoot is null || _scanRoot is null) return;

        var parent = _scopedRoot.Parent;
        _scopedRoot = parent is null || ReferenceEquals(parent, _scanRoot) ? null : parent;
        UpdateScopeState();
        Refresh(_filterPercent, _includeFreeSpace);
    }

    [RelayCommand]
    private void GoToRoot()
    {
        if (_scopedRoot is null) return;
        _scopedRoot = null;
        UpdateScopeState();
        Refresh(_filterPercent, _includeFreeSpace);
    }

    public void SetContextTarget(FsItem? node)
    {
        ContextTarget = node;
        ContextTargetPath = node is null ? string.Empty : BuildFullPath(AncestorChain(node));
    }

    public bool IsFreeSpace(FsItem? node) => node?.Name == DriveScanMetadata.FreeSpaceName;

    public bool IsFiltered(FsItem? node) => node?.Name == ChartDisplayMetadata.FilteredName;

    public bool IsInaccessible(FsItem? node) => node?.Name == DriveScanMetadata.InaccessibleName;

    public bool SuppressesContextMenu(FsItem? node) =>
        node is null || IsFreeSpace(node) || IsFiltered(node) || IsInaccessible(node);

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (ContextTarget is not null)
            _fileSystem.ShowInExplorer(ContextTargetPath);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteAsync()
    {
        if (ContextTarget is null) return;
        if (!await _dialogs.ConfirmAsync("Move to Recycle Bin", "Move to Recycle Bin?\n\n" + ContextTargetPath))
            return;
        if (!_fileSystem.TryDelete(ContextTargetPath, permanent: false, out var error))
            await _dialogs.ShowInfoAsync("Delete failed", error ?? "Delete failed.");
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeletePermanentlyAsync()
    {
        if (ContextTarget is null) return;
        var prompt = "Are you sure you want to permanently delete " + ContextTargetPath + "? This cannot be undone.";
        if (!await _dialogs.ConfirmAsync("Permanently delete", prompt))
            return;
        if (!_fileSystem.TryDelete(ContextTargetPath, permanent: true, out var error))
            await _dialogs.ShowInfoAsync("Delete failed", error ?? "Delete failed.");
    }

    private FsItem GetDisplayRoot() => _scopedRoot ?? GetBaseChartRoot();

    private FsItem GetBaseChartRoot() =>
        _includeFreeSpace && _isDriveScan
            ? _scanRoot!
            : _chartRootWithoutSynthetic ?? _scanRoot!;

    private void UpdateScopeState()
    {
        IsScoped = _scopedRoot is not null;
        if (_scopedRoot is null || _scanRoot is null)
        {
            _displayRootPath = _targetPath;
            ScopeLabel = string.Empty;
            return;
        }

        _displayRootPath = _scopedRoot.TryGetPathFrom(_scanRoot, out var path) ? path : _targetPath;
        ScopeLabel = $"{_displayRootPath}  |  {Humanize.FsItem(_scopedRoot)}";
    }

    private IReadOnlyList<FsItem> AncestorChain(FsItem node)
    {
        // From the scope/base root down to the hovered node (root-first).
        var stop = _scopedRoot ?? GetBaseChartRoot();
        var chain = new List<FsItem>();
        for (var current = node; current is not null && !ReferenceEquals(current, stop); current = current.Parent)
            chain.Add(current);
        chain.Reverse();
        return chain;
    }

    private string BuildFullPath(IReadOnlyList<FsItem> chain)
    {
        var path = _displayRootPath;
        foreach (var item in chain)
            path = Path.Combine(path, item.Name);
        return path;
    }

    private static FsItem? BuildChartRootWithoutSyntheticEntries(FsItem scanRoot, bool isDrive)
    {
        if (!isDrive || scanRoot.Items is null) return null;

        var stripped = new FsItem(scanRoot.Name, scanRoot.Size, scanRoot.IsDir)
        {
            Items = scanRoot.Items.Skip(DriveScanMetadata.SyntheticEntryCount).ToList()
        };
        return stripped;
    }
}
