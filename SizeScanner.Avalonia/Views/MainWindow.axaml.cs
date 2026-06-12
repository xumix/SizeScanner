// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

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
            vm.SaveOnClose((int)Width, (int)Height, splitterDistance: 0);
        base.OnClosing(e);
    }
}
