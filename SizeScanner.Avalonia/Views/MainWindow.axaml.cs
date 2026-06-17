// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia.Controls;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.ViewModels;

namespace SizeScanner.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public MainWindow(MainWindowViewModel viewModel, ITopLevelProvider topLevelProvider)
        : this()
    {
        DataContext = viewModel;

        if (viewModel.InitialWindowWidth > 0) Width = viewModel.InitialWindowWidth;
        if (viewModel.InitialWindowHeight > 0) Height = viewModel.InitialWindowHeight;

        Opened += (_, _) =>
        {
            topLevelProvider.Register(this);
            viewModel.Initialize();
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var splitterDistance = vm.InaccessiblePaneVisible
                ? (int)Math.Round(MainContentGrid.ColumnDefinitions[2].ActualWidth)
                : (int)Math.Round(vm.InaccessiblePaneWidth);
            vm.SaveOnClose((int)Width, (int)Height, splitterDistance);
        }
        base.OnClosing(e);
    }
}
