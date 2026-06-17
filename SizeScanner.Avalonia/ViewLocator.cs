// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SizeScanner.Avalonia.ViewModels;
using SizeScanner.Avalonia.Views;

namespace SizeScanner.Avalonia;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param) => param switch
    {
        null => null,
        ChartViewModel => new ChartView(),
        MainWindowViewModel => new MainWindow(),
        ViewModelBase vm => new TextBlock { Text = "Not Found: " + vm.GetType().Name },
        _ => null
    };

    public bool Match(object? data) => data is ViewModelBase;
}
