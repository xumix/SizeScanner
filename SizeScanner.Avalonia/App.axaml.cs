// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Services;
using SizeScanner.Avalonia.ViewModels;
using SizeScanner.Avalonia.Views;

namespace SizeScanner.Avalonia;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();

            desktop.MainWindow = provider.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITopLevelProvider, TopLevelProvider>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<IFileSystemActions, WindowsFileSystemActions>();
        services.AddSingleton<IElevationService, WindowsElevationService>();
        services.AddSingleton<IDriveProvider, DriveProvider>();
        services.AddSingleton<IFolderPicker, AvaloniaFolderPicker>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();

        services.AddSingleton<ChartViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
