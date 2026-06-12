// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    private readonly ITopLevelProvider _topLevel;

    public AvaloniaDialogService(ITopLevelProvider topLevel) => _topLevel = topLevel;

    public Task<bool> ConfirmAsync(string title, string message) => ShowAsync(title, message, confirm: true);

    public async Task ShowInfoAsync(string title, string message) => await ShowAsync(title, message, confirm: false);

    private async Task<bool> ShowAsync(string title, string message, bool confirm)
    {
        if (_topLevel.TopLevel is not Window owner) return false;

        var result = false;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MinWidth = 360
        };

        var ok = new Button { Content = confirm ? "Yes" : "OK", IsDefault = true, MinWidth = 88 };
        ok.Click += (_, _) => { result = true; dialog.Close(); };
        buttons.Children.Add(ok);

        if (confirm)
        {
            var cancel = new Button { Content = "No", IsCancel = true, MinWidth = 88 };
            cancel.Click += (_, _) => { result = false; dialog.Close(); };
            buttons.Children.Add(cancel);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, MaxWidth = 480 },
                buttons
            }
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
