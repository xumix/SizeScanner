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

    public MainWindow(MainWindowViewModel viewModel, ISettingsStore settingsStore, ITopLevelProvider topLevelProvider)
        : this()
    {
        DataContext = viewModel;

        var settings = settingsStore.Load();
        if (settings.WindowWidth > 0) Width = settings.WindowWidth;
        if (settings.WindowHeight > 0) Height = settings.WindowHeight;

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
