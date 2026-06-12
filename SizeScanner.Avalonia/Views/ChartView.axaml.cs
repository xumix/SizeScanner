// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ScannerCore;
using SizeScanner.Avalonia.ViewModels;

namespace SizeScanner.Avalonia.Views;

public partial class ChartView : UserControl
{
    private SunburstChartControl _chart = null!;
    private ChartViewModel? _subscribedVm;
    private Point? _lastRightClickPosition;

    public ChartView()
    {
        InitializeComponent();
        _chart = this.FindControl<SunburstChartControl>("PART_Chart")!;

        _chart.PointerMoved += OnPointerMoved;
        _chart.PointerExited += OnPointerExited;
        _chart.PointerPressed += OnPointerPressed;
        _chart.ContextRequested += OnChartContextRequested;
        if (_chart.ContextMenu is ContextMenu contextMenu)
            contextMenu.Opening += OnChartContextMenuOpening;

        DataContextChanged += (_, _) => SubscribeViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ChartViewModel? Vm => DataContext as ChartViewModel;

    private void SubscribeViewModel()
    {
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribedVm = Vm;
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChartViewModel.HoverToolTip))
            return;

        if (!ToolTip.GetIsOpen(_chart))
            return;

        if (ToolTip.GetTip(_chart) is ToolTip toolTip)
        {
            toolTip.InvalidateMeasure();
            if (toolTip.Content is Control content)
                content.InvalidateMeasure();
        }
    }

    private FsItem? HitTest(Point position)
    {
        if (Vm is null) return null;
        return Vm.ResolveNode(_chart.HitTestSegment(position));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Vm is null) return;
        var position = e.GetPosition(_chart);
        var node = HitTest(position);
        Vm.Hover(node);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e) => Vm?.ClearHover();

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null) return;
        ToolTip.SetIsOpen(_chart, false);
        var props = e.GetCurrentPoint(_chart).Properties;
        var node = HitTest(e.GetPosition(_chart));

        if (props.IsLeftButtonPressed)
        {
            if (node is not null) Vm.TryScopeAt(node);
            return;
        }

        if (props.IsRightButtonPressed)
        {
            _lastRightClickPosition = e.GetPosition(_chart);
            if (Vm.SuppressesContextMenu(node))
            {
                Vm.SetContextTarget(null);
                e.Handled = true;
                return;
            }

            Vm.SetContextTarget(node);
        }
    }

    private void OnChartContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm is null || !e.TryGetPosition(_chart, out var position))
            return;

        var node = HitTest(position);
        if (!Vm.SuppressesContextMenu(node))
            return;

        Vm.SetContextTarget(null);
        e.Handled = true;
    }

    private void OnChartContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (Vm is null || _lastRightClickPosition is not Point position)
            return;

        var node = HitTest(position);
        if (!Vm.SuppressesContextMenu(node))
            return;

        Vm.SetContextTarget(null);
        e.Cancel = true;
    }
}
