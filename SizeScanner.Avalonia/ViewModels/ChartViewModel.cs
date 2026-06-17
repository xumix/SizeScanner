// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private FsItem? _chartRootWithoutFreeSpace;
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
    [ObservableProperty] private bool _isDeleting;
    [ObservableProperty] private string _deleteStatusText = string.Empty;

    public FsItem? ContextTarget { get; private set; }
    public string ContextTargetPath { get; private set; } = string.Empty;

    public void SetScan(FsItem scanRoot, bool isDrive, string targetPath)
    {
        _scanRoot = scanRoot;
        _isDriveScan = isDrive;
        _targetPath = targetPath;
        _chartRootWithoutFreeSpace = BuildChartRootWithoutFreeSpace(scanRoot, isDrive);
        _scopedRoot = null;
        UpdateScopeState();
    }

    public void Refresh(float filterPercent, bool includeFreeSpace)
    {
        _filterPercent = filterPercent;
        _includeFreeSpace = includeFreeSpace;
        RebuildLayout();
    }

    private void RebuildLayout()
    {
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

    public bool CanScopeTo(FsItem? item) => ChartNodeRules.IsScopable(item);

    public bool TryScopeAt(FsItem node)
    {
        if (!CanScopeTo(node)) return false;
        _scopedRoot = node;
        UpdateScopeState();
        RebuildLayout();
        return true;
    }

    [RelayCommand]
    private void GoUp()
    {
        if (_scopedRoot is null || _scanRoot is null) return;

        var parent = _scopedRoot.Parent;
        _scopedRoot = parent is null || ReferenceEquals(parent, _scanRoot) ? null : parent;
        UpdateScopeState();
        RebuildLayout();
    }

    [RelayCommand]
    private void GoToRoot()
    {
        if (_scopedRoot is null) return;
        _scopedRoot = null;
        UpdateScopeState();
        RebuildLayout();
    }

    public void SetContextTarget(FsItem? node)
    {
        ContextTarget = node;
        ContextTargetPath = node is null ? string.Empty : BuildFullPath(AncestorChain(node));
    }

    public bool IsFreeSpace(FsItem? node) => ChartNodeRules.IsFreeSpace(node);

    public bool IsFiltered(FsItem? node) => ChartNodeRules.IsFiltered(node);

    public bool IsInaccessible(FsItem? node) => ChartNodeRules.IsInaccessible(node);

    public bool SuppressesContextMenu(FsItem? node) => ChartNodeRules.SuppressesContextMenu(node);

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (ContextTarget is not null)
            _fileSystem.ShowInExplorer(ContextTargetPath);
    }

    [RelayCommand]
    private Task DeleteAsync() =>
        DeleteCoreAsync(
            permanent: false,
            title: "Move to Recycle Bin",
            message: "Move to Recycle Bin?\n\n" + ContextTargetPath);

    [RelayCommand]
    private Task DeletePermanentlyAsync() =>
        DeleteCoreAsync(
            permanent: true,
            title: "Permanently delete",
            message: "Are you sure you want to permanently delete " + ContextTargetPath + "? This cannot be undone.");

    private async Task DeleteCoreAsync(bool permanent, string title, string message)
    {
        var target = ContextTarget;
        var path = ContextTargetPath;
        if (target is null) return;

        if (!await _dialogs.ConfirmAsync(title, message))
            return;

        DeleteStatusText = BuildDeleteStatusText(path, permanent);
        IsDeleting = true;

        DeleteResult result;
        try
        {
            result = await _fileSystem.DeleteAsync(path, permanent);
        }
        finally
        {
            IsDeleting = false;
            DeleteStatusText = string.Empty;
        }

        if (!result.Success)
        {
            await _dialogs.ShowInfoAsync("Delete failed", result.Error ?? "Delete failed.");
            return;
        }

        RemoveFromTree(target);
        ClearHover();
        SetContextTarget(null);
        UpdateScopeState();
        RebuildLayout();
    }

    private static string BuildDeleteStatusText(string path, bool permanent) =>
        permanent
            ? "Deleting permanently: " + path
            : "Moving to Recycle Bin: " + path;

    private void RemoveFromTree(FsItem node)
    {
        var parent = node.Parent;
        if (parent?.Items is null)
            return;

        if (!parent.Items.Remove(node))
            return;

        if (ReferenceEquals(parent, _scanRoot))
            _chartRootWithoutFreeSpace?.Items?.Remove(node);

        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
            ancestor.Size -= node.Size;
    }

    private FsItem GetDisplayRoot() => _scopedRoot ?? GetBaseChartRoot();

    private FsItem GetBaseChartRoot() =>
        _includeFreeSpace && _isDriveScan
            ? _scanRoot!
            : _chartRootWithoutFreeSpace ?? _scanRoot!;

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
        // From the scope/base root down to the hovered node (root-first). Walk via
        // Parent pointers, which always reference the real scan tree. GetBaseChartRoot()
        // may return a free-space-stripped clone whose children still point at _scanRoot,
        // so stop on the real root to avoid overshooting and including the drive root.
        var stop = _scopedRoot ?? _scanRoot;
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

    private static FsItem? BuildChartRootWithoutFreeSpace(FsItem scanRoot, bool isDrive)
    {
        if (!isDrive || scanRoot.Items is null) return null;

        // "Hide free space" must remove only the [Free space] entry; the
        // [Inaccessible] synthetic entry stays visible on the chart.
        var items = scanRoot.Items.ToList();
        if (items.Count > DriveScanMetadata.FreeSpaceIndex
            && items[DriveScanMetadata.FreeSpaceIndex].Name == DriveScanMetadata.FreeSpaceName)
        {
            items.RemoveAt(DriveScanMetadata.FreeSpaceIndex);
        }

        return new FsItem(scanRoot.Name, scanRoot.Size, scanRoot.IsDir) { Items = items };
    }
}
